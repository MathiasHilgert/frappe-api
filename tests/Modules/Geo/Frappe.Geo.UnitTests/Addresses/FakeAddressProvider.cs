using Frappe.Geo.Application.Addresses;
using Frappe.Geo.Application.Territory;
using Frappe.Localization;

namespace Frappe.Geo.UnitTests.Addresses;

/// <summary>An address provider answering canned places, or unavailable when <see cref="Down"/>.</summary>
internal sealed class FakeAddressProvider : IAddressProvider
{
    public const string Known = "place-1";

    public bool Down { get; set; }

    public List<AddressSearch> Searches { get; } = [];

    public List<(AddressReference Reference, AddressSession Session)> Resolutions { get; } = [];

    public Dictionary<string, ProviderAddress> Places { get; } = new(StringComparer.Ordinal)
    {
        [Known] = new ProviderAddress(Known, "Av. Corrientes 1234, Buenos Aires", "Avenida Corrientes", "1234", "C1043", "Buenos Aires", "AR", ["Capital Federal", "CABA"], new Coordinates(-34.6, -58.38)),
    };

    public string Name => "fake";

    public TimeSpan SessionLifetime => TimeSpan.FromMinutes(15);

    public double EstimatedCostUsd(AddressOperation operation) => operation == AddressOperation.Suggest ? 0.01 : 0.02;

    public Task<IReadOnlyList<ProviderSuggestion>> SuggestAsync(AddressSearch search, CancellationToken cancellationToken)
    {
        Searches.Add(search);
        return Down
            ? throw new AddressProviderUnavailableException("down")
            : Task.FromResult<IReadOnlyList<ProviderSuggestion>>([new ProviderSuggestion(Known, "Av. Corrientes 1234, Buenos Aires", "Av. Corrientes 1234", "Buenos Aires")]);
    }

    public Task<ProviderAddress?> ResolveAsync(AddressReference reference, AddressSession session, LanguageTag language, CancellationToken cancellationToken)
    {
        Resolutions.Add((reference, session));
        if (Down)
        {
            throw new AddressProviderUnavailableException("down");
        }

        return Task.FromResult(reference.Provider == Name ? Places.GetValueOrDefault(reference.ProviderId) : null);
    }
}
