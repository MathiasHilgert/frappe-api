using Alba;
using Frappe.Platform.Health;
using Frappe.Platform.Http;
using Frappe.Platform.Modularity;
using Frappe.Platform.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

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
        WebApplicationBuilder builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = environment }
        );
        builder.AddHealth();
        builder.AddHttp();
        builder.Services.ConfigureHttpJsonOptions(static options =>
            options.SerializerOptions.AddKernelValueObjectConverters()
        );
        builder.AddFrappeModules(module);
        configure?.Invoke(builder);
        return AlbaHost.For(builder, static app => app.MapFrappeEndpoints());
    }
}
