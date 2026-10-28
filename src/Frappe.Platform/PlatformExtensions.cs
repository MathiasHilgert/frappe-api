using Frappe.Platform.Health;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Frappe.Platform;

/// <summary>
/// The platform defaults every Frappé process shares. <c>Program.cs</c> calls
/// <see cref="AddFrappePlatform{TBuilder}"/> before building and <see cref="MapFrappeEndpoints"/> after.
/// </summary>
public static class PlatformExtensions
{
    /// <summary>Registers the platform services; <c>Platform:Role</c> is validated when the host starts.</summary>
    public static TBuilder AddFrappePlatform<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder
            .Services.AddOptionsWithValidateOnStart<PlatformOptions, PlatformOptionsValidator>()
            .BindConfiguration(PlatformOptions.SectionName);
        builder.AddHealth();
        return builder;
    }

    /// <summary>Maps the platform endpoints. Health is mapped in every role so every replica can be probed.</summary>
    public static WebApplication MapFrappeEndpoints(this WebApplication app)
    {
        app.MapHealthEndpoints();
        return app;
    }
}
