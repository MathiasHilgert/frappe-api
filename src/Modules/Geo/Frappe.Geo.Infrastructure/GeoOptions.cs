using System.ComponentModel.DataAnnotations;

namespace Frappe.Geo.Infrastructure;

internal sealed class GeoOptions
{
    public const string SectionName = "Geo";

    // How long clients may cache a catalogue and a replica serves its snapshot before reading it again. The
    // catalogues change only when a deployment seeds new reference data, which also changes their ETag.
    [Range(typeof(TimeSpan), "00:00:01", "365.00:00:00")]
    public TimeSpan CatalogueMaxAge { get; init; } = TimeSpan.FromDays(1);
}
