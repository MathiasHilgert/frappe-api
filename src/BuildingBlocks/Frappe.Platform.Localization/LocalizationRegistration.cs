using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Frappe.Platform.Localization;

public static class LocalizationRegistration
{
    /// <summary>
    /// Supported locales, negotiation, the request locale and the message catalogue. Named apart from ASP.NET Core's
    /// <c>AddLocalization</c>, which this replaces for system strings.
    /// </summary>
    public static IServiceCollection AddPlatformLocalization(this IServiceCollection services)
    {
        services.AddSingleton<IValidateOptions<LocalizationOptions>, LocalizationOptionsValidator>();
        services.AddOptions<LocalizationOptions>().BindConfiguration(LocalizationOptions.SectionName).ValidateOnStart();
        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<LocalizationOptions>>().Value;
            return new SupportedLocales(options.SupportedLocales, options.DefaultLocale);
        });
        services.AddSingleton<LocaleNegotiator>();
        services.AddSingleton<IMessageCatalog, MessageCatalog>();
        services.AddScoped<RequestLocale>();
        services.AddScoped<IRequestLocale>(sp => sp.GetRequiredService<RequestLocale>());
        services.TryAddScoped<IPreferredLocaleSource, NoPreferredLocale>();
        return services;
    }

    /// <summary>Adds the catalogues embedded in <paramref name="assembly"/> (see <see cref="MessageCatalogSource"/>).</summary>
    public static IServiceCollection AddMessageCatalog(this IServiceCollection services, Assembly assembly) =>
        services.AddSingleton(new MessageCatalogSource(assembly));

    /// <summary>Place after authentication, so a stored preference can be read, and before authorization, so 401/403 carry it.</summary>
    public static IApplicationBuilder UsePlatformLocalization(this IApplicationBuilder app) =>
        app.UseMiddleware<LocalizationMiddleware>();
}
