using Frappe.Identity.Application.Principals;
using Frappe.Identity.Domain;
using Frappe.Identity.Domain.Principals;
using Frappe.SharedKernel.Validation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;

namespace Frappe.Identity.Infrastructure.Persistence;

// Public so Wolverine builds it inline and sees the DbContext behind it, which turns on the EF transaction and outbox.
public sealed class PrincipalRepository(IdentityDbContext context) : IPrincipalRepository
{
    public Task<Principal?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        context.Principals.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    // Raw SQL so the lookup uses lower(email), the expression the unique index is built on. Table and column names come
    // from the EF mapping, so a schema, table or column rename cannot leave this query behind.
    public Task<Principal?> GetByEmailAsync(Email email, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(email);

        var sql = EmailLookupSql(context.Model);
        return context.Principals
            .FromSqlRaw(sql, new NpgsqlParameter("email", email.Value))
            .FirstOrDefaultAsync(cancellationToken);
    }

    // Only mapped identifiers are inlined; the email stays a parameter.
    private static string EmailLookupSql(IModel model)
    {
        var principals = model.FindEntityType(typeof(Principal))!;
        var owners = principals.FindNavigation(nameof(Principal.Owner))!.TargetEntityType;
        var principalsTable = StoreObjectIdentifier.Table(principals.GetTableName()!, principals.GetSchema());
        var ownersTable = StoreObjectIdentifier.Table(owners.GetTableName()!, owners.GetSchema());

        var principalId = Column(principals, nameof(Principal.Id), principalsTable);
        var ownerPrincipalId = Column(owners, PrincipalConfiguration.PrincipalIdColumn, ownersTable);
        var ownerEmail = Column(owners, nameof(OwnerAccount.Email), ownersTable);

        return string.Join(
            '\n',
            $"SELECT p.*, p.xmin FROM {QualifiedTable(principals)} p",
            $"JOIN {QualifiedTable(owners)} o ON o.{ownerPrincipalId} = p.{principalId}",
            $"WHERE lower(o.{ownerEmail}) = lower(@email)");
    }

    private static string Column(IEntityType entity, string property, StoreObjectIdentifier table) =>
        $"\"{entity.FindProperty(property)!.GetColumnName(table)}\"";

    private static string QualifiedTable(IEntityType entity) =>
        $"\"{entity.GetSchema() ?? IdentityModule.Schema}\".\"{entity.GetTableName()}\"";

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

    public async Task<IReadOnlyList<Principal>> ListPendingRegisteredBeforeAsync(
        DateTimeOffset cutoff, int limit, CancellationToken cancellationToken) =>
        await context.Principals
            .Where(p => p.Owner.Status == OwnerStatus.Pending && p.RegisteredAt < cutoff)
            .OrderBy(p => p.RegisteredAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public void Add(Principal principal) => context.Principals.Add(principal);

    public void Remove(Principal principal) => context.Principals.Remove(principal);

    public async Task<Result> SaveAsync(CancellationToken cancellationToken) =>
        await SaveUnlessChangedAsync(cancellationToken) ? Result.Success() : Result.Failure(IdentityErrors.ConcurrentChange);

    public async Task<bool> SaveUnlessChangedAsync(CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            // Nothing was written; forget the stale state so no later save of this unit of work (the transactional
            // middleware's included) retries it.
            context.ChangeTracker.Clear();
            return false;
        }
    }

    public async Task<Result> SaveClaimingEmailAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await SaveAsync(cancellationToken);
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
