using Frappe.Geo.Contracts;
using Frappe.Geo.Contracts.ValueObjects;
using Frappe.Geo.Domain.Addresses;
using Frappe.Geo.Domain.Cities;
using Frappe.Geo.Domain.Countries;
using Frappe.Geo.Infrastructure.Catalogues;
using Frappe.Geo.Infrastructure.Countries;

namespace Frappe.Geo.Infrastructure.Addresses;

/// <summary>Validates address parts against the city catalogue and the countries' address formats.</summary>
internal sealed class AddressValidator(
    ICityCatalogue cities,
    CountrySnapshot countries,
    AddressFormatSnapshot formats
) : IAddressValidator
{
    public ValueTask<ErrorOr<Success>> ValidatePostalCode(
        CountryCode country,
        PostalCode postalCode,
        CancellationToken cancellationToken
    ) =>
        ValueTask.FromResult(
            countries.Find(country, Locale.English) is null
                ? UnknownFilter.Field(CountryErrors.NotFound)
                : formats.PostalCodeFormatOf(country).Validate(postalCode)
        );

    public async ValueTask<ErrorOr<Success>> ValidateAsync(
        CityId city,
        StreetLine streetLine,
        PostalCode? postalCode,
        CityId? currentCity,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(streetLine);
        if (city.Value == Guid.Empty)
        {
            return AddressErrors.CityRequired;
        }

        ErrorOr<GeoCity> found = await cities.GetCityAsync(city, cancellationToken);
        if (found.IsError)
        {
            return found.Errors.ConvertAll(UnknownFilter.Field);
        }

        List<Error> errors = [];
        if (found.Value.Status == ReferenceStatus.Retired && city != currentCity)
        {
            errors.Add(CityErrors.Retired);
        }

        ErrorOr<Success> postal = formats
            .PostalCodeFormatOf(found.Value.Country)
            .Validate(postalCode);
        if (postal.IsError)
        {
            errors.AddRange(postal.Errors);
        }

        return errors.Count == 0 ? Result.Success : errors;
    }
}
