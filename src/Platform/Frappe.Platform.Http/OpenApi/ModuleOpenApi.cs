using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Frappe.Platform.Http.OpenApi;

/// <summary>
/// Brings a module's XML-doc descriptions into the platform's OpenAPI document. The generator that turns XML docs into
/// descriptions runs per compilation and only on that compilation's own <c>AddOpenApi()</c> call, and the platform's
/// call cannot see module assemblies; but a second real call would register the document's services twice.
/// </summary>
public static class ModuleOpenApi
{
    /// <summary>
    /// Runs the module's own <c>AddOpenApi()</c> call on a scratch collection and keeps only the document options it
    /// configures (the module's XML-doc transformers), so the document and its services stay registered once.
    /// </summary>
    /// <param name="services">The host's services.</param>
    /// <param name="addOpenApi">The module's call, written in the module: <c>static scratch =&gt; scratch.AddOpenApi()</c>.</param>
    /// <returns>The same services, for chaining.</returns>
    public static IServiceCollection AddModuleOpenApiDescriptions(
        this IServiceCollection services,
        Action<IServiceCollection> addOpenApi
    )
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(addOpenApi);
        ServiceCollection scratch = [];
        addOpenApi(scratch);
        foreach (
            ServiceDescriptor options in scratch.Where(static service =>
                service.ServiceType == typeof(IConfigureOptions<OpenApiOptions>)
            )
        )
        {
            services.Add(options);
        }

        return services;
    }
}
