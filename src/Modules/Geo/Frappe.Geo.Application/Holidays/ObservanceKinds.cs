using Frappe.Geo.Domain.Holidays;

namespace Frappe.Geo.Application.Holidays;

/// <summary>The names of the observance kinds on the wire, in both directions, written once.</summary>
public static class ObservanceKinds
{
    private static readonly Dictionary<ObservanceKind, string> Names = new()
    {
        [ObservanceKind.Holiday] = "holiday",
        [ObservanceKind.TouristNonWorkingDay] = "tourist_non_working_day",
        [ObservanceKind.NonWorkingDay] = "non_working_day",
        [ObservanceKind.CommunityNonWorkingDay] = "community_non_working_day",
    };

    private static readonly Dictionary<string, ObservanceKind> Kinds = Names.ToDictionary(p => p.Value, p => p.Key, StringComparer.Ordinal);

    public static string NameOf(ObservanceKind kind) => Names[kind];

    public static bool TryParse(string? name, out ObservanceKind kind) => Kinds.TryGetValue(name ?? string.Empty, out kind);
}
