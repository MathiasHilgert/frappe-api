using System.Text.Json;

namespace Frappe.Platform.Serialization;

/// <summary>JSON support for the Kernel value objects, which stay free of any serializer.</summary>
public static class KernelJsonExtensions
{
    /// <summary>
    /// Adds a converter for every Kernel value object: codes, ids and locales are written as their JSON string,
    /// <c>MinorUnit</c> as a number and <c>Address</c> as <c>{ streetLine, cityId, postalCode }</c> (postal code
    /// omitted when absent). Reading goes through each value object's <c>Create</c>, so invalid input throws
    /// <see cref="JsonException"/>.
    /// </summary>
    /// <param name="options">The options to extend.</param>
    /// <returns>The same options.</returns>
    public static JsonSerializerOptions AddKernelValueObjectConverters(
        this JsonSerializerOptions options
    )
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Converters.Add(new CountryCodeJsonConverter());
        options.Converters.Add(new CurrencyCodeJsonConverter());
        options.Converters.Add(new MinorUnitJsonConverter());
        options.Converters.Add(new SubdivisionCodeJsonConverter());
        options.Converters.Add(new PostalCodeJsonConverter());
        options.Converters.Add(new TimeZoneIdJsonConverter());
        options.Converters.Add(new LocaleJsonConverter());
        options.Converters.Add(new CityIdJsonConverter());
        options.Converters.Add(new AddressJsonConverter());
        return options;
    }
}
