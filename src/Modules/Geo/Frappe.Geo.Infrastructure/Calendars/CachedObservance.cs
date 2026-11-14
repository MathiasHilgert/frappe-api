using Frappe.Geo.Domain.Holidays;
using NodaTime;

namespace Frappe.Geo.Infrastructure.Calendars;

/// <summary>An observance as the shared cache stores it: plain JSON, read back through the domain's own factory.</summary>
internal sealed record CachedObservance(LocalDate Date, string Name, ObservanceKind Kind, LocalDate? MovedFrom, string? Community, string? Citation, Uri? Text)
{
    public static CachedObservance Of(Observance observance) => new(
        observance.Date, observance.Name, observance.Kind, observance.MovedFrom, observance.Community, observance.LegalBasis?.Citation, observance.LegalBasis?.Text);

    /// <summary>The observance it was written from; a stored observance was valid, so this cannot fail.</summary>
    public Observance ToObservance() =>
        Observance.Create(Date, Name, Kind, MovedFrom, Community, Citation is null ? null : new LegalBasis(Citation, Text)).Value;
}
