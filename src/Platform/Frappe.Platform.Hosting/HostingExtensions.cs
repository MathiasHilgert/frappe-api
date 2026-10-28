using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Frappe.Platform.Hosting;

/// <summary>Registers the process role every adapter can read; <c>Platform:Role</c> is validated on start.</summary>
public static class HostingExtensions
{
    /// <summary>
    /// Binds <see cref="PlatformOptions"/> from <c>Platform</c> and validates it on start. Every adapter
    /// that reads the role calls it, so a second call registers nothing.
    /// </summary>
    public static TBuilder AddHostRole<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        if (
            builder.Services.Any(static service =>
                service.ServiceType == typeof(IValidateOptions<PlatformOptions>)
                && service.ImplementationType == typeof(PlatformOptionsValidator)
            )
        )
        {
            return builder;
        }

        builder
            .Services.AddOptionsWithValidateOnStart<PlatformOptions, PlatformOptionsValidator>()
            .BindConfiguration(PlatformOptions.SectionName);
        return builder;
    }
}
