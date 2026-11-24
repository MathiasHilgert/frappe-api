using Frappe.Messaging.Storage.Relay;
using Frappe.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Frappe.Identity.Infrastructure.Persistence;

/// <summary>
/// The Identity database as the Migrator sees it: schema <c>identity</c> migrated as the owner, the Api's role <c>frappe_identity</c> with data
/// access on the schema, and the Worker's narrow role <c>frappe_identity_worker</c>, which reads and writes none of the module's business data.
/// </summary>
public sealed class IdentityDatabase : IDatabaseModule
{
    /// <summary>The Api's login role: every command and query of the module.</summary>
    public const string ApiRole = "frappe_identity";

    /// <summary>The name of the Api's role under <c>Roles</c> in the Migrator's configuration.</summary>
    public const string ApiRoleConfigName = "Identity";

    /// <summary>The Worker's login role: the relay and, as the jobs arrive, the sweep and the purge, and nothing else.</summary>
    public const string WorkerRole = "frappe_identity_worker";

    /// <summary>The name of the Worker's role under <c>Roles</c> in the Migrator's configuration.</summary>
    public const string WorkerRoleConfigName = "IdentityWorker";

    /// <inheritdoc />
    public string Name => "identity";

    /// <inheritdoc />
    public string Schema => IdentityDbContext.Schema;

    /// <inheritdoc />
    public IReadOnlyList<DatabaseRole> Roles { get; } =
    [
        new(ApiRole, ApiRoleConfigName, RoleGrant.Schema),
        new(WorkerRole, WorkerRoleConfigName, RoleGrant.Custom(GrantWorkerAsync)),
    ];

    /// <inheritdoc />
    public async Task MigrateAsync(string ownerConnectionString, CancellationToken cancellationToken)
    {
        await using var context = new IdentityDbContext(ModuleDatabase.Options<IdentityDbContext>(ownerConnectionString, IdentityDbContext.Schema));
        await context.Database.MigrateAsync(cancellationToken);
    }

    /// <summary>
    /// The statements that add what the Worker does on top of the relay's grants, in order: it writes outbox rows, because the sweep and the
    /// purge raise their events through the aggregates. No business table is granted yet; the sweep and the purge ask for their columns.
    /// </summary>
    internal static IReadOnlyList<string> WorkerStatements(string schema) =>
    [
        $"GRANT INSERT ON TABLE {schema}.outbox_messages TO {WorkerRole}",
    ];

    /// <summary>
    /// Leaves the Worker's role with the relay's and the purge's grants (<see cref="RelayRoles"/>, which first revokes everything the role
    /// held) plus <see cref="WorkerStatements"/>, in a second transaction. Between the two commits the role holds only the relay grants, never
    /// more than the final set, so a failure in between fails closed.
    /// </summary>
    private static async Task GrantWorkerAsync(NpgsqlConnection owner, CancellationToken cancellationToken)
    {
        await RelayRoles.GrantAsync(owner, IdentityDbContext.Schema, WorkerRole, [], cancellationToken);
        await using var transaction = await owner.BeginTransactionAsync(cancellationToken);
        foreach (var statement in WorkerStatements(IdentityDbContext.Schema))
        {
            await using var command = new NpgsqlCommand(statement, owner, transaction);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
