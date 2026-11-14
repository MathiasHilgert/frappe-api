using Frappe.Geo.Infrastructure.ReferenceData;
using NodaTime;

namespace Frappe.Geo.Infrastructure.Territory;

/// <summary>A row of <c>geo.localities</c>: a populated place, keyed by country and source id, with its search form.</summary>
internal sealed class LocalityRow : IReferenceRow<LocalityRow>
{
    public required string CountryCode { get; init; }

    public required string SourceId { get; init; }

    public required string SubdivisionCode { get; set; }

    public string? AreaId { get; set; }

    public string? LocalGovernmentId { get; set; }

    public required string Name { get; set; }

    public required string SearchName { get; set; }

    public required string Category { get; set; }

    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    public Instant? RetiredAt { get; set; }

    string IReferenceRow<LocalityRow>.Key => CountryCode + ":" + SourceId;

    public bool SameAs(LocalityRow desired) =>
        (SubdivisionCode, AreaId, LocalGovernmentId, Name, Category, Latitude, Longitude)
        == (desired.SubdivisionCode, desired.AreaId, desired.LocalGovernmentId, desired.Name, desired.Category, desired.Latitude, desired.Longitude);

    public void CopyFrom(LocalityRow desired) =>
        (SubdivisionCode, AreaId, LocalGovernmentId, Name, SearchName, Category, Latitude, Longitude)
            = (desired.SubdivisionCode, desired.AreaId, desired.LocalGovernmentId, desired.Name, desired.SearchName, desired.Category, desired.Latitude, desired.Longitude);
}
