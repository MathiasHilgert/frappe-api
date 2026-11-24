using Frappe.Http;
using Frappe.Modules;
using Frappe.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Frappe.Api.IntegrationTests;

/// <summary>The real Api, composed as deployed (its settings file included) on an in-memory server, with no module unless a test lists one.</summary>
internal static class ApiHost
{
    /// <summary>A Valkey nobody listens on: the settings only need to be valid, and the cache is optional for readiness.</summary>
    private const string UnreachableValkey = "127.0.0.1:1";

    /// <summary>What a deploy sets for the real module lists: the database, the address hash key and the webhook secret (placeholders).</summary>
    public static readonly Dictionary<string, string?> Deployed = new()
    {
        ["ConnectionStrings:Notifications"] = "Host=unreachable.invalid;Database=frappe;Username=frappe_notifications",
        ["Notifications:AddressHash:CurrentKeyId"] = "1",
        ["Notifications:AddressHash:Keys:1"] = "bG9jYWwtb25seS1hZGRyZXNzLWhhc2gta2V5LTMyLWJ5dGVzIQ==",
        ["Notifications:Email:Resend:WebhookSecret"] = "bG9jYWwtb25seS1yZXNlbmQtd2ViaG9vay1zaWduaW5nLXNlY3JldA==",
    };

    public static async Task<WebApplication> StartAsync(
        IReadOnlyCollection<IModule>? modules = null,
        IReadOnlyCollection<IHttpModule>? endpoints = null,
        IReadOnlyDictionary<string, string?>? settings = null,
        string environment = "Testing")
    {
        modules ??= [];
        endpoints ??= [];
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddJsonFile(Path.Combine(RepositoryRoot.FullPath, "src/Hosts/Frappe.Api/appsettings.json"), optional: false);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Valkey"] = UnreachableValkey });
        builder.Configuration.AddInMemoryCollection(settings ?? new Dictionary<string, string?>());
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        builder.AddFrappeApi(modules, endpoints);
        var app = builder.Build();
        app.MapFrappeApi(endpoints);
        await app.StartAsync();
        return app;
    }
}
