using Alba;
using Frappe.Platform.Health;
using Frappe.Platform.Http;
using Frappe.Platform.Modularity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Frappe.Platform.IntegrationTests.Http;

// The HTTP surface and the given modules, mapped as Program maps them, without a database or a bus.
internal static class HttpHost
{
    public static Task<IAlbaHost> StartAsync(
        IModule module,
        Action<WebApplicationBuilder>? configure = null,
        string environment = "Development"
    )
    {
        WebApplicationBuilder builder = Builder(module, environment);
        configure?.Invoke(builder);
        return AlbaHost.For(builder, static app => app.MapFrappeEndpoints());
    }

    // On real Kestrel, for the limits the in-memory test server does not enforce, such as the body size.
    public static async Task<WebApplication> StartKestrelAsync(IModule module)
    {
        WebApplicationBuilder builder = Builder(module, "Development");
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        WebApplication app = builder.Build();
        app.MapFrappeEndpoints();
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }

    private static WebApplicationBuilder Builder(IModule module, string environment)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = environment }
        );
        builder.AddHealth();
        builder.AddHttp();
        builder.AddFrappeModules(module);
        return builder;
    }
}
