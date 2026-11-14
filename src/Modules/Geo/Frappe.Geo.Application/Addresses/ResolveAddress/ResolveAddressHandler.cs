using Frappe.Geo.Domain.Countries;
using Frappe.Localization;
using Frappe.Results;
using Frappe.UseCases;

namespace Frappe.Geo.Application.Addresses.ResolveAddress;

/// <summary>
/// An answered resolution closes its session; it counts as completed only when the session was opened by a suggestion
/// within its lifetime, so a resolution of its own session (another module re-validating) is no session (S15).
/// </summary>
internal sealed class ResolveAddressHandler(IAddressProvider provider, ISubdivisionNames subdivisions, IAddressSessions sessions, GeoMetrics metrics)
    : IQueryHandler<ResolveAddress, AddressSnapshot>
{
    public async Task<Result<AddressSnapshot>> HandleAsync(ResolveAddress query, CancellationToken cancellationToken)
    {
        var reference = ReferenceOf(query.Reference);
        var session = AddressSession.Create(query.Session);
        var language = query.Language is null ? query.RequestLanguage : LanguageTag.Create(query.Language);
        var valid = Result.Combine(reference, session, language);
        if (!valid.IsSuccess)
        {
            return Result.Failure<AddressSnapshot>(valid.Errors);
        }

        var calls = new AddressProviderCalls(provider, metrics);
        var place = await calls.RunAsync(
            AddressOperation.Resolve,
            p => p.ResolveAsync(reference.Value, session.Value, language.Value, cancellationToken),
            GeoErrors.AddressNotFound);
        if (!place.IsSuccess)
        {
            return Result.Failure<AddressSnapshot>(place.Errors);
        }

        if (await sessions.CloseAsync(provider.Name, session.Value, cancellationToken))
        {
            metrics.AddressSessionCompleted(provider.Name);
        }

        return await NormalizeAsync(place.Value, cancellationToken);
    }

    /// <summary>A reference Geo issued; <c>geo.parameter.required</c> when absent or blank.</summary>
    private static Result<AddressReference> ReferenceOf(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return GeoErrors.ParameterRequired("reference");
        }

        return AddressReference.Decode(raw) is { } decoded ? decoded : GeoErrors.AddressReferenceInvalid();
    }

    /// <summary>The provider's country must be an ISO code and its subdivision one Geo knows by name or alias; anything else is left out and counted (S15).</summary>
    private async Task<AddressSnapshot> NormalizeAsync(ProviderAddress place, CancellationToken cancellationToken)
    {
        var country = CountryCode.Create(place.CountryCode);
        string? subdivision = null;
        if (!country.IsSuccess)
        {
            metrics.AddressUnmapped(null, "country");
        }
        else
        {
            subdivision = place.SubdivisionNames.Count == 0 ? null : await subdivisions.FindCodeAsync(country.Value.Value, place.SubdivisionNames, cancellationToken);
            if (subdivision is null)
            {
                metrics.AddressUnmapped(country.Value.Value, "subdivision");
            }
        }

        return new AddressSnapshot(
            new AddressReference(provider.Name, place.ProviderId).Encode(),
            place.FormattedAddress,
            place.Street,
            place.StreetNumber,
            place.PostalCode,
            place.Locality,
            country.IsSuccess ? country.Value.Value : null,
            subdivision,
            place.Coordinates is { IsValid: true } point ? point : null);
    }
}
