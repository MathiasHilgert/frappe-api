using Frappe.Identity.Infrastructure.Persistence;
using Frappe.Messaging.Storage.Relay;
using Frappe.Persistence;
using Frappe.Testing.Containers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Frappe.Identity.IntegrationTests;

/// <summary>A scratch database with the Identity database set up by the Migrator's phases, as the Migrator sets it up.</summary>
internal static class MigratedIdentity
{
    public const string ApiPassword = "identity-api-test-password";
    public const string WorkerPassword = "identity-worker-test-password";

    private static readonly string[] Roles = [IdentityDatabase.ApiRole, IdentityDatabase.WorkerRole];

    /// <summary>
    /// Runs <paramref name="test"/> with the owner's connection string of a clone of the migrated Identity template database;
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
        "identity|" + TemplateDatabase.VersionOf(typeof(IdentityDatabase).Assembly)
        + "|" + TemplateDatabase.VersionOf(typeof(RelayRoles).Assembly)
        + "|" + TemplateDatabase.VersionOf(typeof(DatabaseProvisioning).Assembly);

    /// <summary>Runs every phase of the Migrator over the Identity database, as the owner.</summary>
    public static Task MigrateAsync(string owner) => DatabaseProvisioning.RunAsync(
        owner,
        [new IdentityDatabase()],
        new Dictionary<string, string>
        {
            [IdentityDatabase.ApiRoleConfigName] = ApiPassword,
            [IdentityDatabase.WorkerRoleConfigName] = WorkerPassword,
        },
        new DatabaseOptions(),
        NullLogger.Instance,
        CancellationToken.None);
}
