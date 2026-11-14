using System.Diagnostics;
using Frappe.Results;

namespace Frappe.Geo.Application.Addresses;

/// <summary>
/// The one way Geo calls its address provider: timed, counted by operation and outcome, priced when answered, and an
/// provider that is not configured turned into <see cref="GeoErrors.AddressesNotConfigured"/> and one that does not answer
/// into <see cref="GeoErrors.AddressesUnavailable"/>. The caller's cancellation propagates.
/// </summary>
internal sealed class AddressProviderCalls(IAddressProvider provider, GeoMetrics metrics)
{
    public IAddressProvider Provider => provider;

    /// <summary>Runs <paramref name="call"/>; a <see langword="null"/> answer counts as not found.</summary>
    public async Task<Result<T>> RunAsync<T>(AddressOperation operation, Func<IAddressProvider, Task<T?>> call, Func<Error> notFound)
        where T : class
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            var answer = await call(provider);
            Record(operation, answer is null ? "not_found" : "ok", started);
            return answer is null ? notFound() : answer;
        }
        catch (AddressProviderNotConfiguredException)
        {
            Record(operation, "not_configured", started);
            return GeoErrors.AddressesNotConfigured();
        }
        catch (AddressProviderUnavailableException)
        {
            Record(operation, "unavailable", started);
            return GeoErrors.AddressesUnavailable();
        }
    }

    private void Record(AddressOperation operation, string outcome, long started) =>
        metrics.AddressProviderCalled(
            provider.Name,
            operation == AddressOperation.Suggest ? "suggest" : "resolve",
            outcome,
            Stopwatch.GetElapsedTime(started),
            outcome == "ok" ? provider.EstimatedCostUsd(operation) : 0);
}
