using Frappe.Localization;

namespace Frappe.Geo.Application.Addresses;

/// <summary>
/// The provider of a host that composes none, or whose provider lacks its credentials: every address answers
/// <see cref="GeoErrors.AddressesNotConfigured"/>, so the host still starts and the rest of Geo still works. A host
/// replaces it by composing a real one from a source package.
/// </summary>
public sealed class UnconfiguredAddressProvider : IAddressProvider
{
    /// <summary>Its name. It issues no reference, so the name never reaches one.</summary>
    public const string ProviderName = "unconfigured";

    public string Name => ProviderName;

    public Task<IReadOnlyList<ProviderSuggestion>> SuggestAsync(AddressSearch search, CancellationToken cancellationToken) =>
        throw new AddressProviderNotConfiguredException();

    public Task<ProviderAddress?> ResolveAsync(AddressReference reference, AddressSession session, LanguageTag language, CancellationToken cancellationToken) =>
        throw new AddressProviderNotConfiguredException();
}
