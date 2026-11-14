using Frappe.Geo.Infrastructure.ReferenceData;
using NodaTime;

namespace Frappe.Geo.Infrastructure.Countries;

/// <summary>A row of <c>geo.countries</c>, with its names and its currencies in use as owned rows.</summary>
internal sealed class CountryRow : IReferenceRow<CountryRow>
{
    public required string Code { get; init; }

    public required string Alpha3 { get; set; }

    public required string Numeric { get; set; }

    public Instant? RetiredAt { get; set; }

    public List<CountryNameRow> Names { get; init; } = [];

    public List<CountryCurrencyRow> Currencies { get; init; } = [];

    string IReferenceRow<CountryRow>.Key => Code;

    public bool SameAs(CountryRow desired) =>
        Alpha3 == desired.Alpha3
        && Numeric == desired.Numeric
        && Names.OrderBy(n => n.Language.Value, StringComparer.Ordinal).Select(n => (n.Language, n.Text))
            .SequenceEqual(desired.Names.OrderBy(n => n.Language.Value, StringComparer.Ordinal).Select(n => (n.Language, n.Text)))
        && Currencies.OrderBy(c => c.Rank).Select(c => c.CurrencyCode)
            .SequenceEqual(desired.Currencies.OrderBy(c => c.Rank).Select(c => c.CurrencyCode), StringComparer.Ordinal);

    public void CopyFrom(CountryRow desired)
    {
        Alpha3 = desired.Alpha3;
        Numeric = desired.Numeric;
        ReferenceDataApplier.SyncChildren(Names, desired.Names, n => n.Language, (stored, wanted) => stored.Text = wanted.Text);
        ReferenceDataApplier.SyncChildren(Currencies, desired.Currencies, c => c.CurrencyCode, (stored, wanted) => stored.Rank = wanted.Rank);
    }
}
