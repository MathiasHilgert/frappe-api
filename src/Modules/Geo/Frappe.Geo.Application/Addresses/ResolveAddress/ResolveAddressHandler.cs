using Frappe.Geo.Domain.Countries;
using Frappe.Localization;
using Frappe.Results;
using Frappe.UseCases;

namespace Frappe.Geo.Application.Addresses.ResolveAddress;

/// <summary>
/// A valid reference, session and language reach the provider; its place is normalized to the ISO codes Geo knows.
/// </summary>
internal sealed class ResolveAddressHandler(IAddressProvider provider, ISubdivisionNames subdivisions)
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

        var place = await new AddressProviderCalls(provider).RunAsync(
            p => p.ResolveAsync(reference.Value, session.Value, language.Value, cancellationToken),
            GeoErrors.AddressNotFound);
        if (!place.IsSuccess)
        {
            return Result.Failure<AddressSnapshot>(place.Errors);
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

    /// <summary>The provider's country must be an ISO code and its subdivision one Geo knows by name or alias; anything else is left out.</summary>
    private async Task<AddressSnapshot> NormalizeAsync(ProviderAddress place, CancellationToken cancellationToken)
    {
        var country = CountryCode.Create(place.CountryCode);
        var subdivision = !country.IsSuccess || place.SubdivisionNames.Count == 0
            ? null
            : await subdivisions.FindCodeAsync(country.Value.Value, place.SubdivisionNames, cancellationToken);

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
