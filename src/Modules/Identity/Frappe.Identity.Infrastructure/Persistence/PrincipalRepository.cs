using Frappe.Identity.Application.Principals;
using Frappe.Identity.Domain;
using Frappe.Identity.Domain.Principals;
using Frappe.SharedKernel.Validation;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Frappe.Identity.Infrastructure.Persistence;

// Public so Wolverine builds it inline and sees the DbContext behind it, which turns on the EF transaction and outbox.
public sealed class PrincipalRepository(IdentityDbContext context) : IPrincipalRepository
{
    public Task<Principal?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        context.Principals.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    // Raw SQL so the lookup uses lower(email), the expression the unique index is built on.
    public Task<Principal?> GetByEmailAsync(Email email, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(email);

        return context.Principals
            .FromSql($"""
                SELECT p.* FROM identity.principals p
                JOIN identity.owner_accounts o ON o.principal_id = p.id
                WHERE lower(o.email) = lower({email.Value})
                """)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<Principal?> GetBySessionTokenHashAsync(string tokenHash, CancellationToken cancellationToken) =>
        context.Principals.FirstOrDefaultAsync(p => p.Sessions.Any(s => s.TokenHash == tokenHash), cancellationToken);

    public Task<Principal?> GetByCodeIdAsync(Guid codeId, CodePurpose purpose, CancellationToken cancellationToken) =>
        context.Principals.FirstOrDefaultAsync(p => p.Codes.Any(c => c.Id == codeId && c.Purpose == purpose), cancellationToken);

    public void Add(Principal principal) => context.Principals.Add(principal);

    public async Task<Result> SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success();
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
    }
}
