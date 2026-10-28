using Frappe.Platform.Health;
using Frappe.Platform.Hosting;
using Frappe.Platform.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;

namespace Frappe.Platform;

/// <summary>
/// The platform defaults every Frappé process shares. <c>Program.cs</c> calls
/// <see cref="AddFrappePlatform{TBuilder}"/> before building and <see cref="MapFrappeEndpoints"/> after.
/// </summary>
public static class PlatformExtensions
{
    /// <summary>
    /// Registers the platform services; <c>Platform:Role</c> and <c>ConnectionStrings:Frappe</c> are
    /// validated when the host starts.
    /// </summary>
    public static TBuilder AddFrappePlatform<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.AddHostRole();
        builder.AddHealth();
        builder.AddPersistence();
        return builder;
    }

    /// <summary>Maps the platform endpoints. Health is mapped in every role so every replica can be probed.</summary>
    public static WebApplication MapFrappeEndpoints(this WebApplication app)
    {
        app.MapHealthEndpoints();
        return app;
    }
}
