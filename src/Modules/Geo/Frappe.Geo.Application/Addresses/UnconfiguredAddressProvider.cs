using Frappe.Localization;

namespace Frappe.Geo.Application.Addresses;

/// <summary>
/// The provider of a host that composes none, or whose provider lacks its credentials: every address answers
/// <see cref="GeoErrors.AddressesNotConfigured"/>, so the host still starts and the rest of Geo still works, and its
/// calls are counted under <see cref="ProviderName"/>, never under a provider that is not there. A host replaces it by
/// composing a real one from a source package.
/// </summary>
public sealed class UnconfiguredAddressProvider : IAddressProvider
{
    /// <summary>The provider label of its metrics. It issues no reference, so the name never reaches one.</summary>
    public const string ProviderName = "unconfigured";

    public string Name => ProviderName;

    /// <summary>It answers no suggestion, so it opens no session.</summary>
    public TimeSpan SessionLifetime => TimeSpan.Zero;

    public double EstimatedCostUsd(AddressOperation operation) => 0;

    public Task<IReadOnlyList<ProviderSuggestion>> SuggestAsync(AddressSearch search, CancellationToken cancellationToken) =>
        throw new AddressProviderNotConfiguredException();

    public Task<ProviderAddress?> ResolveAsync(AddressReference reference, AddressSession session, LanguageTag language, CancellationToken cancellationToken) =>
        throw new AddressProviderNotConfiguredException();
}
