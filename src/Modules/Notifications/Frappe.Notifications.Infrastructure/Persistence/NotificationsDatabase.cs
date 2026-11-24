using Frappe.Messaging.Storage.Relay;
using Frappe.Notifications.Infrastructure.Deliveries;
using Frappe.Notifications.Infrastructure.Preferences;
using Frappe.Notifications.Infrastructure.ProviderEvents;
using Frappe.Notifications.Infrastructure.Suppressions;
using Frappe.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Frappe.Notifications.Infrastructure.Persistence;

/// <summary>
/// The Notifications database as the Migrator sees it: schema <c>notifications</c> migrated as the owner, the Api's role
/// <c>frappe_notifications</c> with data access on the schema, and the Worker's narrow role <c>frappe_notifications_worker</c>.
/// </summary>
public sealed class NotificationsDatabase : IDatabaseModule
{
    /// <summary>The Api's login role: commands, the webhook and the unsubscribe.</summary>
    public const string ApiRole = "frappe_notifications";

    /// <summary>The name of the Api's role under <c>Roles</c> in the Migrator's configuration.</summary>
    public const string ApiRoleConfigName = "Notifications";

    /// <summary>The Worker's login role: the relay, the consumers and the purge, and nothing else.</summary>
    public const string WorkerRole = "frappe_notifications_worker";

    /// <summary>The name of the Worker's role under <c>Roles</c> in the Migrator's configuration.</summary>
    public const string WorkerRoleConfigName = "NotificationsWorker";

    /// <inheritdoc />
    public string Name => "notifications";

    /// <inheritdoc />
    public string Schema => NotificationsDbContext.Schema;

    /// <inheritdoc />
    public IReadOnlyList<DatabaseRole> Roles { get; } =
    [
        new(ApiRole, ApiRoleConfigName, RoleGrant.Schema),
        new(WorkerRole, WorkerRoleConfigName, RoleGrant.Custom(GrantWorkerAsync)),
    ];

    /// <inheritdoc />
    public async Task MigrateAsync(string ownerConnectionString, CancellationToken cancellationToken)
    {
        await using var context = new NotificationsDbContext(
            ModuleDatabase.Options<NotificationsDbContext>(ownerConnectionString, NotificationsDbContext.Schema));
        await context.Database.MigrateAsync(cancellationToken);
    }

    /// <summary>The statements that add what the Worker does on top of the relay's grants, in order.</summary>
    internal static IReadOnlyList<string> WorkerStatements(string schema) =>
    [
        $"GRANT INSERT ON TABLE {schema}.inbox_messages TO {WorkerRole}",
        $"GRANT INSERT, SELECT, DELETE ON TABLE {schema}.{DeliveryConfiguration.Table} TO {WorkerRole}",
        $"GRANT SELECT ON TABLE {schema}.{SuppressionConfiguration.Table} TO {WorkerRole}",
        $"GRANT SELECT ON TABLE {schema}.{PreferenceConfiguration.Table} TO {WorkerRole}",
        $"GRANT DELETE ON TABLE {schema}.{ProviderEventRowConfiguration.Table} TO {WorkerRole}",
        $"GRANT SELECT (received_at) ON TABLE {schema}.{ProviderEventRowConfiguration.Table} TO {WorkerRole}",
    ];

    /// <summary>
    /// Leaves the Worker's role with the relay's and the purge's grants (<see cref="RelayRoles"/>, which first revokes everything the
    /// role held) plus <see cref="WorkerStatements"/>. These run in two transactions: <see cref="RelayRoles"/> commits its own, then
    /// <see cref="WorkerStatements"/> run in a second one. Between the two commits the role holds only the relay grants, never more than
    /// the final set, so a failure in between fails closed. A single transaction needs a <see cref="RelayRoles"/> overload that takes
    /// extra statements or a transaction (a follow-up in Platform).
    /// </summary>
    private static async Task GrantWorkerAsync(NpgsqlConnection owner, CancellationToken cancellationToken)
    {
        await RelayRoles.GrantAsync(owner, NotificationsDbContext.Schema, WorkerRole, [], cancellationToken);
        await using var transaction = await owner.BeginTransactionAsync(cancellationToken);
        foreach (var statement in WorkerStatements(NotificationsDbContext.Schema))
        {
            await using var command = new NpgsqlCommand(statement, owner, transaction);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
