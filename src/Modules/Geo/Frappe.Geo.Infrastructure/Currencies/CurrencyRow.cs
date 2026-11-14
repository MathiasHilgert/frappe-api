using Frappe.Geo.Infrastructure.ReferenceData;
using NodaTime;

namespace Frappe.Geo.Infrastructure.Currencies;

/// <summary>A row of <c>geo.currencies</c>, with its names and symbols as owned rows.</summary>
internal sealed class CurrencyRow : IReferenceRow<CurrencyRow>
{
    public required string Code { get; init; }

    public required int MinorUnitDigits { get; set; }

    public Instant? RetiredAt { get; set; }

    public List<CurrencyNameRow> Names { get; init; } = [];

    string IReferenceRow<CurrencyRow>.Key => Code;

    public bool SameAs(CurrencyRow desired) =>
        MinorUnitDigits == desired.MinorUnitDigits
        && Names.OrderBy(n => n.Language.Value, StringComparer.Ordinal).Select(n => (n.Language, n.Name, n.Symbol))
            .SequenceEqual(desired.Names.OrderBy(n => n.Language.Value, StringComparer.Ordinal).Select(n => (n.Language, n.Name, n.Symbol)));

    public void CopyFrom(CurrencyRow desired)
    {
        MinorUnitDigits = desired.MinorUnitDigits;
        ReferenceDataApplier.SyncChildren(Names, desired.Names, n => n.Language, (stored, wanted) =>
        {
            stored.Name = wanted.Name;
            stored.Symbol = wanted.Symbol;
        });
    }
}
