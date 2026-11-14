using Frappe.Aggregates;
using Frappe.UseCases;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace Frappe.Persistence;

/// <summary>
/// The unit of work of a module over its own context. On its own, a unit opens its transaction only to commit: the
/// handler's reads and its calls to other systems (downloads of minutes) hold no transaction and no connection, and
/// everything it staged is written by one <c>SaveChanges</c> (the outbox interceptor's rows included) and committed
/// together. Inside a transaction somebody else owns (an inbox consumer's), the unit first writes what that owner
/// staged, then runs inside a savepoint: committing writes the unit's changes and releases the savepoint, leaving the
/// commit to the owner; rolling back returns to the savepoint, so the owner's work survives.
/// <para>
/// Before each of its saves, the unit hands the pending domain events of the tracked aggregates to their
/// <see cref="IDomainEventHandler{TEvent}"/>s (read models, for example), whose changes that same save writes. It only
/// reads the events: the outbox interceptor still takes them during the save. Once the unit's own transaction committed,
/// it runs their <see cref="IAfterCommitDomainEventHandler{TEvent}"/>s (cache invalidation), never after a rollback.
/// Inside somebody else's transaction a released savepoint is not a commit, so the unit runs no after-commit handler
/// there.
/// </para>
/// </summary>
internal sealed class EfUnitOfWork<TContext>(TContext db, IServiceProvider services, ILogger logger) : IUnitOfWork
    where TContext : DbContext
{
    private const string Savepoint = "frappe_unit_of_work";

    /// <summary>Upper bound for the after-commit handlers; independent of the command's token, which must not cut them.</summary>
    private const int AfterCommitTimeoutSeconds = 10;

    /// <summary>The transaction the unit nests in, or <see langword="null"/> when the unit owns its own.</summary>
    private IDbContextTransaction? _ambient;

    public async Task BeginAsync(CancellationToken cancellationToken)
    {
        _ambient = db.Database.CurrentTransaction;
        if (_ambient is not null)
        {
            await SaveAsync(cancellationToken);
            await _ambient.CreateSavepointAsync(Savepoint, cancellationToken);
        }
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        var committed = db.Database.CurrentTransaction is null;
        var events = await SaveAsync(cancellationToken);
        if (committed)
        {
            // Outside any transaction, the save was its own and is committed now.
            await AfterCommitAsync(events);
        }
    }

    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        if (_ambient is not null)
        {
            await SaveAsync(cancellationToken);
            await _ambient.ReleaseSavepointAsync(Savepoint, cancellationToken);
            return;
        }

        IReadOnlyList<IDomainEvent> events;
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            events = await SaveAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        await AfterCommitAsync(events);
    }

    public async Task RollbackAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (_ambient is not null)
            {
                await _ambient.RollbackToSavepointAsync(Savepoint, cancellationToken);
            }
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    /// <summary>Runs the in-unit handlers of the pending domain events, then writes everything in one save.</summary>
    /// <returns>The events the save carried, for the after-commit handlers.</returns>
    private async Task<IReadOnlyList<IDomainEvent>> SaveAsync(CancellationToken cancellationToken)
    {
        var events = db.ChangeTracker.Entries<AggregateRoot>()
            .SelectMany(entry => entry.Entity.DomainEvents)
            .ToArray();
        foreach (var domainEvent in events)
        {
            await DomainEventDispatch.HandleAsync(services, domainEvent, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        return events;
    }

    private async Task AfterCommitAsync(IReadOnlyList<IDomainEvent> events)
    {
        if (events.Count == 0)
        {
            return;
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(AfterCommitTimeoutSeconds));
        foreach (var domainEvent in events)
        {
            await DomainEventDispatch.AfterCommitAsync(services, domainEvent, logger, timeout.Token);
        }
    }
}
