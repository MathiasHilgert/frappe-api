using Npgsql;

namespace Frappe.Persistence;

/// <summary>What a login role may do once its participant migrated: the data access of its whole schema, or a narrower grant of its own.</summary>
public sealed class RoleGrant
{
    private readonly Func<NpgsqlConnection, string, string, CancellationToken, Task> _apply;

    private RoleGrant(Func<NpgsqlConnection, string, string, CancellationToken, Task> apply) => _apply = apply;

    /// <summary>
    /// Data access (no DDL) on every table, sequence and invoker function of the participant's schema
    /// (<see cref="ModuleRoles.GrantDataAccessAsync"/>).
    /// </summary>
    public static RoleGrant Schema { get; } = new(ModuleRoles.GrantDataAccessAsync);

    /// <summary>
    /// A grant narrower than the schema: <paramref name="grant"/> runs as the owner after the migrations, on every run,
    /// and must be idempotent.
    /// </summary>
    public static RoleGrant Custom(Func<NpgsqlConnection, CancellationToken, Task> grant)
    {
        ArgumentNullException.ThrowIfNull(grant);
        return new RoleGrant((owner, _, _, cancellationToken) => grant(owner, cancellationToken));
    }

    internal Task ApplyAsync(NpgsqlConnection owner, string role, string schema, CancellationToken cancellationToken) =>
        _apply(owner, role, schema, cancellationToken);
}
