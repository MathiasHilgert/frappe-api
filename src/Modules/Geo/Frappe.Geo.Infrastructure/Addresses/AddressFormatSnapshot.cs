using System.Collections.Frozen;
using Frappe.Geo.Application.Countries;
using Frappe.Geo.Contracts.ValueObjects;
using Frappe.Geo.Domain.Addresses;
using Frappe.Geo.Infrastructure.Catalogues;
using Microsoft.EntityFrameworkCore;

namespace Frappe.Geo.Infrastructure.Addresses;

/// <summary>The address formats in memory, loaded once at startup, each with its compiled postal-code rule.</summary>
internal sealed class AddressFormatSnapshot
{
    private static readonly PostalCodeFormat _anyPostalCode = PostalCodeFormat.Create(
        null,
        required: false
    );

    private FrozenDictionary<string, AddressFormat>? _formats;

    private FrozenDictionary<string, AddressFormat> Loaded =>
        _formats ?? throw new InvalidOperationException("The address formats are not loaded.");

    public async Task<LoadedCatalogue> LoadAsync(
        GeoDbContext db,
        CancellationToken cancellationToken
    )
    {
        List<AddressFormatRow> rows = await db.Set<AddressFormatRow>()
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        _formats = rows.ToFrozenDictionary(
            static row => row.CountryCode,
            static row => new AddressFormat(
                PostalCodeFormat.Create(row.PostalCodePattern, row.PostalCodeRequired),
                new AddressFormatDto(
                    row.PostalCodePattern,
                    row.PostalCodeExamples,
                    row.PostalCodeRequired,
                    [.. row.RequiredFields.Select(Parse<AddressField>)],
                    Parse<SubdivisionLabel>(row.SubdivisionLabel)
                )
            ),
            StringComparer.Ordinal
        );
        return new LoadedCatalogue(
            rows.Count,
            [
                .. rows.Select(static row =>
                        $"address_format|{row.CountryCode}|{row.PostalCodePattern}|{string.Join(',', row.PostalCodeExamples)}|{row.PostalCodeRequired}|{string.Join(',', row.RequiredFields)}|{row.SubdivisionLabel}"
                    )
                    .Order(StringComparer.Ordinal),
            ]
        );
    }

    /// <summary>Returns a country's address format, or null when none is held.</summary>
    public AddressFormatDto? Find(CountryCode country) =>
        Loaded.TryGetValue(country.Value, out AddressFormat? format) ? format.Dto : null;

    /// <summary>Returns a country's postal-code rule; a country without a format accepts any code.</summary>
    public PostalCodeFormat PostalCodeFormatOf(CountryCode country) =>
        Loaded.TryGetValue(country.Value, out AddressFormat? format)
            ? format.PostalCode
            : _anyPostalCode;

    // The stored snake_case wire value, such as street_line, names the member StreetLine.
    private static TEnum Parse<TEnum>(string value)
        where TEnum : struct, Enum =>
        Enum.Parse<TEnum>(value.Replace("_", "", StringComparison.Ordinal), ignoreCase: true);

    private sealed record AddressFormat(PostalCodeFormat PostalCode, AddressFormatDto Dto);
}
