using System.ComponentModel.DataAnnotations;

namespace Frappe.Geo.Infrastructure;

internal sealed class GeoOptions
{
    public const string SectionName = "Geo";

    // The catalogue changes only when a deployment ships a new TZDB release, which also changes its ETag.
    [Range(typeof(TimeSpan), "00:00:01", "365.00:00:00")]
    public TimeSpan TimezonesMaxAge { get; init; } = TimeSpan.FromDays(1);
}
