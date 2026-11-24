using Frappe.Notifications.Infrastructure.Persistence;
using Frappe.Persistence;
using Microsoft.Extensions.Logging.Abstractions;

namespace Frappe.Notifications.IntegrationTests;

/// <summary>A scratch database with the Notifications database set up by the Migrator's phases, as the Migrator sets it up.</summary>
internal static class MigratedNotifications
{
    public const string ApiPassword = "notifications-api-test-password";
    public const string WorkerPassword = "notifications-worker-test-password";

    private static readonly string[] Roles = [NotificationsDatabase.ApiRole, NotificationsDatabase.WorkerRole];

    /// <summary>Runs <paramref name="test"/> with the owner's connection string of a database the Notifications module is migrated in.</summary>
    public static Task RunAsync(PostgresServer server, Func<string, Task> test) =>
        ScratchDatabase.RunAsync(server, Roles, async owner =>
        {
            await MigrateAsync(owner);
            await test(owner);
        });

    /// <summary>Runs every phase of the Migrator over the Notifications database, as the owner.</summary>
    public static Task MigrateAsync(string owner) => DatabaseProvisioning.RunAsync(
        owner,
        [new NotificationsDatabase()],
        new Dictionary<string, string>
        {
            [NotificationsDatabase.ApiRoleConfigName] = ApiPassword,
            [NotificationsDatabase.WorkerRoleConfigName] = WorkerPassword,
        },
        new DatabaseOptions(),
        NullLogger.Instance,
        CancellationToken.None);
}
