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
/// <see cref="IDomainEventHandler{TEvent}"/>s (read models, for example), whose changes that same save writes. The
/// save carries them to the outbox when the module has one (its interceptor takes them), and the unit takes whatever is
/// left once the save succeeded, so a later save never hands the same event to a handler twice. Once the unit's own transaction committed,
/// it runs their <see cref="IAfterCommitDomainEventHandler{TEvent}"/>s (cache invalidation), never after a rollback.
/// Inside somebody else's transaction a released savepoint is not a commit, so the unit runs no after-commit handler
/// there.
/// </para>
/// </summary>
internal sealed class EfUnitOfWork<TContext>(
    TContext db,
    IServiceProvider services,
    ILogger logger,
    CommitRejectionMap? rejections = null,
    PersistenceMetrics? metrics = null,
    string module = "unknown") : IUnitOfWork
    where TContext : DbContext
{
    private const string Savepoint = "frappe_unit_of_work";

    private const string DeferredCheckSavepoint = "frappe_deferred_check";

    /// <summary>How many times a rule may repair the staged changes and save again before the failure goes through.</summary>
    private const int MaxRepairs = 3;

    /// <summary>Upper bound for the after-commit handlers; independent of the command's token, which must not cut them.</summary>
    private const int AfterCommitTimeoutSeconds = 10;

    /// <summary>The transaction the unit nests in, or <see langword="null"/> when the unit owns its own.</summary>
    private IDbContextTransaction? _ambient;

    public async Task BeginAsync(CancellationToken cancellationToken)
    {
        // Kept apart until the savepoint exists: a rejected begin leaves no ambient, so the rollback never targets a savepoint
        // that was not created (or an outer unit's savepoint of the same name).
        _ambient = null;
        var current = db.Database.CurrentTransaction;
        if (current is not null)
        {
            await SaveAsync(cancellationToken);
            await current.CreateSavepointAsync(Savepoint, cancellationToken);
            _ambient = current;
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
            await CheckDeferredAsync(_ambient, restoreDeferral: true, cancellationToken);
            await _ambient.ReleaseSavepointAsync(Savepoint, cancellationToken);
            return;
        }

        IReadOnlyList<IDomainEvent> events;
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            events = await SaveAsync(cancellationToken);
            await CheckDeferredAsync(transaction, restoreDeferral: false, cancellationToken);
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
        var aggregates = db.ChangeTracker.Entries<AggregateRoot>()
            .Select(entry => entry.Entity)
            .Where(aggregate => aggregate.DomainEvents.Count > 0)
            .ToArray();
        var events = aggregates.SelectMany(aggregate => aggregate.DomainEvents).ToArray();
        foreach (var domainEvent in events)
        {
            await DomainEventDispatch.HandleAsync(services, domainEvent, cancellationToken);
        }

        await SaveRejectingAsync(cancellationToken);

        // Without an outbox nobody took them during the save: each event is handled once per save, saved or not.
        foreach (var aggregate in aggregates)
        {
            aggregate.DequeueDomainEvents();
        }

        return events;
    }

    /// <summary>
    /// One <c>SaveChanges</c>; when the storage refuses it, the module's rules (<see cref="CommitRejectionMap"/>) decide:
    /// a rejection is thrown (UOW-05), a repair saves again (never running the domain events a second time), and a
    /// failure no rule claims passes through unchanged. A repair that keeps failing the same way gives up after
    /// <see cref="MaxRepairs"/> repairs with a <see cref="CommitConflictException"/> (a conflict the client may retry).
    /// </summary>
    private async Task SaveRejectingAsync(CancellationToken cancellationToken)
    {
        var repairs = 0;
        using var _ = CommitRejectionInterceptor.EnterUnitOfWork();
        while (true)
        {
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (Exception exception) when (rejections is not null && exception is not OperationCanceledException)
            {
                var resolution = await rejections.ResolveAsync(new CommitFailure(db, exception, cancellationToken));
                if (resolution is null)
                {
                    throw;
                }

                if (!resolution.IsRepair)
                {
                    metrics?.CommitRejected(module);
                    throw resolution.Rejection!;
                }

                if (++repairs > MaxRepairs)
                {
                    metrics?.RepairsExhausted(module);
                    throw new CommitConflictException("The commit kept failing after its staged changes were repaired.", exception);
                }
            }
        }
    }

    /// <summary>
    /// When the module mapped a deferrable constraint (<see cref="CommitRejectionMap.OnDeferred{TRejection}"/>), checks the
    /// deferred constraints now, while the transaction is alive, instead of letting the commit fail without a way to read.
    /// A failure goes back to a savepoint (the transaction stays usable, so the rule can read the storage) and becomes the
    /// rule's rejection; one no rule claims passes through unchanged. A module without such rules pays nothing.
    /// On a passing check inside somebody else's transaction the unit rolls back to the savepoint, so <c>SET CONSTRAINTS</c>
    /// does not outlive the unit (a later unit keeps its deferral and the owner's commit checks again); on its own
    /// transaction it only releases the savepoint, so the checks and the locks they took still count at the commit.
    /// </summary>
    private async Task CheckDeferredAsync(IDbContextTransaction transaction, bool restoreDeferral, CancellationToken cancellationToken)
    {
        if (rejections is not { HasDeferredRules: true })
        {
            return;
        }

        await transaction.CreateSavepointAsync(DeferredCheckSavepoint, cancellationToken);
        try
        {
            await db.Database.ExecuteSqlRawAsync("SET CONSTRAINTS ALL IMMEDIATE", cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await transaction.RollbackToSavepointAsync(DeferredCheckSavepoint, CancellationToken.None);
            if (rejections.ResolveDeferred(new CommitFailure(db, exception, cancellationToken)) is { } rejection)
            {
                metrics?.CommitRejected(module);
                throw rejection;
            }

            throw;
        }

        if (restoreDeferral)
        {
            // Going back to the savepoint undoes SET CONSTRAINTS, so a later unit in the same owner transaction keeps
            // its deferral (releasing the savepoint would leave every constraint immediate).
            await transaction.RollbackToSavepointAsync(DeferredCheckSavepoint, cancellationToken);
        }

        await transaction.ReleaseSavepointAsync(DeferredCheckSavepoint, cancellationToken);
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
            await DomainEventDispatch.AfterCommitAsync(services, domainEvent, logger, metrics, module, timeout.Token);
        }
    }
}
