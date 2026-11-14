using Frappe.Geo.Domain.Countries;
using Frappe.Geo.Domain.Territory;

namespace Frappe.Geo.Domain.Holidays;

/// <summary>
/// Where a calendar applies: a whole country (<c>AR</c>) or one of its first-level subdivisions (<c>AR-X</c>). A
/// subdivision's calendar holds only its own observances; the country's apply to it too.
/// </summary>
public sealed record Jurisdiction
{
    private Jurisdiction(string countryCode, string? subdivisionCode)
    {
        CountryCode = countryCode;
        SubdivisionCode = subdivisionCode;
    }

    public string CountryCode { get; }

    public string? SubdivisionCode { get; }

    /// <summary><c>AR</c> or <c>AR-X</c>: the stable key a calendar is stored and announced under.</summary>
    public string Value => SubdivisionCode ?? CountryCode;

    public bool IsCountry => SubdivisionCode is null;

    /// <summary>The country this jurisdiction belongs to, itself when it is one.</summary>
    public Jurisdiction Country => IsCountry ? this : new(CountryCode, null);

    public static Jurisdiction Of(CountryCode country)
    {
        ArgumentNullException.ThrowIfNull(country);
        return new(country.Value, null);
    }

    public static Jurisdiction Of(SubdivisionCode subdivision)
    {
        ArgumentNullException.ThrowIfNull(subdivision);
        return new(subdivision.CountryCode, subdivision.Value);
    }

    /// <summary>The jurisdiction a stored key names; the key was written by <see cref="Value"/>, so it is well formed.</summary>
    public static Jurisdiction FromKey(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return key.Length == 2 ? new(key, null) : new(key[..2], key);
    }

    public override string ToString() => Value;
}
