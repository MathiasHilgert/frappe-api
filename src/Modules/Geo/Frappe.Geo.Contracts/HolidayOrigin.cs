using System.Text.Json.Serialization;

namespace Frappe.Geo.Contracts;

/// <summary>How a public holiday got its date.</summary>
public enum HolidayOrigin
{
    /// <summary>A date the law fixes and never moves (in Argentina, inamovible).</summary>
    [JsonStringEnumMemberName("fixed")]
    Fixed,

    /// <summary>A date the law may move to a nearby Monday (in Argentina, trasladable).</summary>
    [JsonStringEnumMemberName("movable")]
    Movable,

    /// <summary>A day decreed to join a holiday to a weekend (in Argentina, a feriado puente).</summary>
    [JsonStringEnumMemberName("bridge")]
    Bridge,

    /// <summary>A one-off day created by decree, such as a state visit (in Argentina, by DNU).</summary>
    [JsonStringEnumMemberName("decreed")]
    Decreed,
}
