using Frappe.Identity.Application.Principals;
using Frappe.Identity.Domain;
using Frappe.Identity.Domain.Principals;
using Frappe.Modularity;
using Frappe.SharedKernel.Validation;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Frappe.Identity.Infrastructure.Persistence;

// Public so Wolverine builds it inline and sees the DbContext behind it, which turns on the EF transaction and outbox.
[InlinedByWolverine]
public sealed class PrincipalRepository(IdentityDbContext context) : IPrincipalRepository
{
    public Task<Principal?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        context.Principals.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    // Email is normalized when created, so a plain equality hits the unique index with no SQL lower().
    public Task<Principal?> GetByEmailAsync(Email email, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(email);
        return context.Principals.FirstOrDefaultAsync(p => p.Owner.Email == email, cancellationToken);
    }

    public Task<Principal?> GetBySessionTokenHashAsync(string tokenHash, CancellationToken cancellationToken) =>
        context.Principals.FirstOrDefaultAsync(p => p.Sessions.Any(s => s.TokenHash == tokenHash), cancellationToken);

    public Task<SessionCredential?> FindSessionCredentialAsync(string tokenHash, CancellationToken cancellationToken) =>
        context.Principals
            .AsNoTracking()
            .SelectMany(p => p.Sessions
                .Where(s => s.TokenHash == tokenHash)
                .Select(s => new SessionCredential(p.Id, p.Type, p.Owner.Status, s.Id, s.CreatedAt, s.LastSeenAt)))
            .FirstOrDefaultAsync(cancellationToken);

    public Task<Principal?> GetByCodeIdAsync(Guid codeId, CodePurpose purpose, CancellationToken cancellationToken) =>
        context.Principals.FirstOrDefaultAsync(p => p.Codes.Any(c => c.Id == codeId && c.Purpose == purpose), cancellationToken);

    public async Task<IReadOnlyList<PurgeCandidate>> ListPurgeCandidatesAsync(
        DateTimeOffset cutoff, PurgeCandidate? after, int limit, CancellationToken cancellationToken)
    {
        var pending = context.Principals.AsNoTracking()
            .Where(p => p.Owner.Status == OwnerStatus.Pending && p.RegisteredAt < cutoff);
        if (after is not null)
        {
            pending = pending.Where(p => p.RegisteredAt > after.RegisteredAt
                || (p.RegisteredAt == after.RegisteredAt && p.Id.CompareTo(after.Id) > 0));
        }

        return await pending
            .OrderBy(p => p.RegisteredAt).ThenBy(p => p.Id)
            .Take(limit)
            .Select(p => new PurgeCandidate(p.Id, p.RegisteredAt))
            .ToListAsync(cancellationToken);
    }

    public void Add(Principal principal) => context.Principals.Add(principal);

    public void Remove(Principal principal) => context.Principals.Remove(principal);

    public async Task<Result> SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (DbUpdateConcurrencyException)
        {
            // Nothing was written; forget the stale state so no later save of this unit of work (the transactional
            // middleware's included) retries it.
            context.ChangeTracker.Clear();
            return Result.Failure(IdentityErrors.ConcurrentChange);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: PrincipalConfiguration.EmailUniqueIndex,
        })
        {
            // Stop tracking the rejected rows, so a later save of this unit of work does not retry them.
            foreach (var entry in exception.Entries)
                entry.State = EntityState.Detached;
            return Result.Failure(IdentityErrors.EmailTaken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Inside a transaction EF Core rolled the statement back to its savepoint, so the transaction lives on;
            // forget the failed state too, so a later save (the transactional middleware's included) does not retry
            // it. The caller decides whether the failure ends its unit of work.
            context.ChangeTracker.Clear();
            throw;
        }
    }
}
