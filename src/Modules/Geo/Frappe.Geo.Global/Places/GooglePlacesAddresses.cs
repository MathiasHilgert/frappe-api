using Frappe.Geo.Application.Addresses;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Frappe.Geo.Global.Places;

/// <summary>Google Places (New) as Geo's address provider, for the hosts that serve addresses.</summary>
public static class GooglePlacesAddresses
{
    /// <summary>
    /// Replaces the unconfigured provider with Google's, bound from <c>Geo:Google:Places</c> (<see cref="GooglePlacesOptions"/>).
    /// No key is required to start: without one the provider is <see cref="UnconfiguredAddressProvider"/>, so addresses
    /// answer not configured and its metrics never name Google. The client has the standard resilience
    /// pipeline, shortened for a user who is typing (2 s per attempt, one retry, 5 s in all), and no request logging,
    /// since a URL carries the place id.
    /// </summary>
    public static IServiceCollection AddGooglePlacesAddresses(this IServiceCollection services)
    {
        services.AddOptions<GooglePlacesOptions>().BindConfiguration(GooglePlacesOptions.Section)
            .Validate(o => o.BaseUrl.IsAbsoluteUri && o.BaseUrl.AbsoluteUri.EndsWith('/'), $"Configuration '{GooglePlacesOptions.Section}:BaseUrl' must be an absolute URL ending in '/'.")
            .ValidateOnStart();
        services.AddHttpClient<GooglePlacesProvider>()
            .RemoveAllLoggers()
            .AddStandardResilienceHandler(resilience =>
            {
                resilience.AttemptTimeout.Timeout = TimeSpan.FromSeconds(2);
                resilience.Retry.MaxRetryAttempts = 1;
                resilience.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(5);
            });
        services.RemoveAll<IAddressProvider>();
        services.AddTransient<IAddressProvider>(provider =>
            string.IsNullOrWhiteSpace(provider.GetRequiredService<IOptions<GooglePlacesOptions>>().Value.ApiKey)
                ? new UnconfiguredAddressProvider()
                : provider.GetRequiredService<GooglePlacesProvider>());
        return services;
    }
}
