using NodaTime;

namespace Frappe.Geo.Application.Sync;

/// <summary>A source changed the observance a manual override decides (by date and name) in a jurisdiction's year.</summary>
public sealed record OverrideConflict(string Jurisdiction, int Year, LocalDate Date, string Name);
