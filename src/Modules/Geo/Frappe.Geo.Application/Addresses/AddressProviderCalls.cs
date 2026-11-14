using Frappe.Results;

namespace Frappe.Geo.Application.Addresses;

/// <summary>
/// The one way Geo calls its address provider: a provider that is not configured turned into
/// <see cref="GeoErrors.AddressesNotConfigured"/> and one that does not answer into
/// <see cref="GeoErrors.AddressesUnavailable"/>. The caller's cancellation propagates.
/// </summary>
internal sealed class AddressProviderCalls(IAddressProvider provider)
{
    public IAddressProvider Provider => provider;

    /// <summary>Runs <paramref name="call"/>; a <see langword="null"/> answer counts as not found.</summary>
    public async Task<Result<T>> RunAsync<T>(Func<IAddressProvider, Task<T?>> call, Func<Error> notFound)
        where T : class
    {
        try
        {
            var answer = await call(provider);
            return answer is null ? notFound() : answer;
        }
        catch (AddressProviderNotConfiguredException)
        {
            return GeoErrors.AddressesNotConfigured();
        }
        catch (AddressProviderUnavailableException)
        {
            return GeoErrors.AddressesUnavailable();
        }
    }
}
