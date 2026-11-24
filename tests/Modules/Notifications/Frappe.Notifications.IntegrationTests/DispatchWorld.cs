using Frappe.Notifications.Channels.Email;
using Frappe.Notifications.Infrastructure;
using Frappe.Notifications.Infrastructure.Persistence;
using Frappe.Notifications.Kinds.Account;
using Frappe.Testing;
using Frappe.Testing.Containers;
using Frappe.UseCases;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Frappe.Notifications.IntegrationTests;

/// <summary>
/// The module as a host composes it, over a migrated database and the logging sender: the Api's services under the Api's role (the
/// command) or the Worker's under the Worker's role (the dispatch), so a missing grant fails the test, not the deploy.
/// </summary>
internal sealed class DispatchWorld : IAsyncDisposable
{
    private readonly ServiceProvider _provider;

    private DispatchWorld(
        string connectionString, bool worker, IReadOnlyDictionary<string, string?>? more = null, Action<IServiceCollection>? configure = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Notifications"] = connectionString,
            ["Notifications:AddressHash:CurrentKeyId"] = "1",
            ["Notifications:AddressHash:Keys:1"] = Convert.ToBase64String(Enumerable.Repeat((byte)9, 32).ToArray()),
            ["Notifications:Unsubscribe:SigningKey"] = Convert.ToBase64String(Enumerable.Repeat((byte)7, 32).ToArray()),
            ["Notifications:Unsubscribe:PageUrl"] = "https://frappe.example/unsubscribe",
            ["Notifications:Unsubscribe:ApiBaseUrl"] = "https://api.frappe.example/",
            ["Notifications:Email:Sender"] = "Logging",
        };
        foreach (var (key, value) in more ?? new Dictionary<string, string?>())
        {
            settings[key] = value;
        }

        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddProvider(Logs));
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        services.AddSingleton<IUseCaseGate, AllowAllUseCaseGate>();
        new NotificationsModule().AddServices(services);
        new AccountKindsModule().AddServices(services);
        if (worker)
        {
            new NotificationsWorkerModule().AddServices(services);
            new EmailChannelWorkerModule().AddServices(services);
        }

        configure?.Invoke(services);
        _provider = services.BuildServiceProvider();
    }

    public CapturedLogs Logs { get; } = new();

    public IServiceProvider Services => _provider;

    /// <summary>The Api's composition connected as the Api's role.</summary>
    public static DispatchWorld ForApi(string owner) =>
        new(ScratchDatabase.LoginOf(owner, NotificationsDatabase.ApiRole, MigratedNotifications.ApiPassword), worker: false);

    /// <summary>The Worker's composition connected as the Worker's role.</summary>
    public static DispatchWorld ForWorker(string owner) =>
        new(ScratchDatabase.LoginOf(owner, NotificationsDatabase.WorkerRole, MigratedNotifications.WorkerPassword), worker: true);

    /// <summary>The Worker's composition with settings and services of the test on top: a producer plug-in, a retention.</summary>
    public static DispatchWorld ForWorker(string owner, IReadOnlyDictionary<string, string?> more, Action<IServiceCollection>? configure = null) =>
        new(ScratchDatabase.LoginOf(owner, NotificationsDatabase.WorkerRole, MigratedNotifications.WorkerPassword), worker: true, more, configure);

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        Logs.Dispose();
    }
}
