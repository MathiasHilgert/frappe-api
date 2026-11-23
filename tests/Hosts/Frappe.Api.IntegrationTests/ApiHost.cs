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

    public static async Task<WebApplication> StartAsync(
        IReadOnlyCollection<IModule>? modules = null,
        IReadOnlyCollection<IHttpModule>? endpoints = null)
    {
        modules ??= [];
        endpoints ??= [];
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddJsonFile(Path.Combine(RepositoryRoot.FullPath, "src/Hosts/Frappe.Api/appsettings.json"), optional: false);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Valkey"] = UnreachableValkey });
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        builder.AddFrappeApi(modules, endpoints);
        var app = builder.Build();
        app.MapFrappeApi(endpoints);
        await app.StartAsync();
        return app;
    }
}
