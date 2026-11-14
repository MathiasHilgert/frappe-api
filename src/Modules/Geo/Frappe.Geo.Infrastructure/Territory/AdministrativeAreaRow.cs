using Frappe.Geo.Infrastructure.ReferenceData;
using NodaTime;

namespace Frappe.Geo.Infrastructure.Territory;

/// <summary>A row of <c>geo.administrative_areas</c>: an area below the first level, keyed by country and source id.</summary>
internal sealed class AdministrativeAreaRow : IReferenceRow<AdministrativeAreaRow>
{
    public required string CountryCode { get; init; }

    public required string SourceId { get; init; }

    public required string SubdivisionCode { get; set; }

    public required int Level { get; set; }

    public required string Category { get; set; }

    public required string Name { get; set; }

    public required bool IsLocalGovernment { get; set; }

    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    public Instant? RetiredAt { get; set; }

    string IReferenceRow<AdministrativeAreaRow>.Key => CountryCode + ":" + SourceId;

    public bool SameAs(AdministrativeAreaRow desired) =>
        (SubdivisionCode, Level, Category, Name, IsLocalGovernment, Latitude, Longitude)
        == (desired.SubdivisionCode, desired.Level, desired.Category, desired.Name, desired.IsLocalGovernment, desired.Latitude, desired.Longitude);

    public void CopyFrom(AdministrativeAreaRow desired) =>
        (SubdivisionCode, Level, Category, Name, IsLocalGovernment, Latitude, Longitude)
            = (desired.SubdivisionCode, desired.Level, desired.Category, desired.Name, desired.IsLocalGovernment, desired.Latitude, desired.Longitude);
}
