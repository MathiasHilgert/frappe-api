using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Frappe.Platform.Hosting;

/// <summary>The process role every adapter can read; <c>Platform:Role</c> is validated on start.</summary>
public static class HostingExtensions
{
    /// <summary>Binds <see cref="PlatformOptions"/> from <c>Platform</c> and validates it on start.</summary>
    public static TBuilder AddHostRole<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder
            .Services.AddOptionsWithValidateOnStart<PlatformOptions, PlatformOptionsValidator>()
            .BindConfiguration(PlatformOptions.SectionName);
        return builder;
    }
}
