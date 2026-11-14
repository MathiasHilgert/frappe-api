using NodaTime;

namespace Frappe.Geo.Domain.Holidays;

/// <summary>
/// One stored entry of a calendar: an observance and the layer it belongs to. A suppressed entry only names the
/// observance (date and name) the override removes. Mutated only by <see cref="HolidayCalendar"/>.
/// </summary>
public sealed class CalendarEntry
{
    private CalendarEntry()
    {
    }

    public CalendarLayer Layer { get; private set; }

    public LocalDate Date { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public ObservanceKind Kind { get; private set; }

    public LocalDate? MovedFrom { get; private set; }

    public string? Community { get; private set; }

    public string? LegalCitation { get; private set; }

    public string? LegalText { get; private set; }

    internal (LocalDate Date, string Name) Key => (Date, Name);

    internal static CalendarEntry Of(CalendarLayer layer, Observance observance) => new()
    {
        Layer = layer,
        Date = observance.Date,
        Name = observance.Name,
        Kind = observance.Kind,
        MovedFrom = observance.MovedFrom,
        Community = observance.Community,
        LegalCitation = observance.LegalBasis?.Citation,
        LegalText = observance.LegalBasis?.Text?.AbsoluteUri,
    };

    internal static CalendarEntry Suppression(LocalDate date, string name) => new() { Layer = CalendarLayer.Suppressed, Date = date, Name = name };

    /// <summary>The observance this entry holds; stored entries were valid when written, so this cannot fail.</summary>
    internal Observance ToObservance() => Observance.Create(Date, Name, Kind, MovedFrom, Community, Basis()).Value;

    private LegalBasis? Basis()
    {
        if (LegalCitation is null)
        {
            return null;
        }

        return new LegalBasis(LegalCitation, LegalText is null ? null : new Uri(LegalText));
    }
}
