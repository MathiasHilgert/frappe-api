using Frappe.Messaging.Storage.Relay;
using Frappe.Notifications.Infrastructure.Persistence;
using Frappe.Persistence;
using Frappe.Testing.Containers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Frappe.Notifications.IntegrationTests;

/// <summary>A scratch database with the Notifications database set up by the Migrator's phases, as the Migrator sets it up.</summary>
internal static class MigratedNotifications
{
    public const string ApiPassword = "notifications-api-test-password";
    public const string WorkerPassword = "notifications-worker-test-password";

    private static readonly string[] Roles = [NotificationsDatabase.ApiRole, NotificationsDatabase.WorkerRole];

    /// <summary>
    /// Runs <paramref name="test"/> with the owner's connection string of a clone of the migrated Notifications template database;
    /// a changed Infrastructure assembly (so a changed migration) builds a new template.
    /// </summary>
    public static Task RunAsync(PostgresServer server, Func<string, Task> test) =>
        TemplateDatabase.RunAsync(server, TemplateKey, MigrateAsync, Roles, test);

    /// <summary>Runs <paramref name="test"/> on a fresh scratch database migrated for it alone, for tests that assert the migration.</summary>
    public static Task RunFreshAsync(PostgresServer server, Func<string, Task> test) =>
        ScratchDatabase.RunAsync(server, [], async owner =>
        {
            await MigrateAsync(owner);
            await test(owner);
        });

    private static readonly string TemplateKey =
        "notifications|" + TemplateDatabase.VersionOf(typeof(NotificationsDatabase).Assembly)
        + "|" + TemplateDatabase.VersionOf(typeof(RelayRoles).Assembly)
        + "|" + TemplateDatabase.VersionOf(typeof(DatabaseProvisioning).Assembly);

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
