using Frappe.Localization;

namespace Frappe.Geo.Application.Addresses;

/// <summary>
/// Outbound port: the service that suggests and resolves addresses (S11), interchangeable and never shown to clients.
/// An adapter only fetches and maps; ids, sessions, normalization to ISO codes and metrics belong to Geo. It throws
/// <see cref="AddressProviderUnavailableException"/> when it is not configured or does not answer, and lets the
/// caller's cancellation propagate.
/// </summary>
public interface IAddressProvider
{
    /// <summary>A short lower-case name, carried inside every <see cref="AddressReference"/> (<c>google</c>).</summary>
    string Name { get; }

    /// <summary>The places that match what the user typed so far, best first.</summary>
    Task<IReadOnlyList<ProviderSuggestion>> SuggestAsync(AddressSearch search, CancellationToken cancellationToken);

    /// <summary>The place of <paramref name="reference"/>, closing <paramref name="session"/>; <see langword="null"/> when the provider does not know it (or it is another provider's).</summary>
    Task<ProviderAddress?> ResolveAsync(AddressReference reference, AddressSession session, LanguageTag language, CancellationToken cancellationToken);
}
