using System.Text.Json.Serialization;

namespace Frappe.Geo.Contracts;

/// <summary>Whether a reference row is in use upstream; a withdrawn row is retired, never deleted.</summary>
public enum ReferenceStatus
{
    /// <summary>The row is in use.</summary>
    [JsonStringEnumMemberName("active")]
    Active,

    /// <summary>The row was withdrawn upstream; stored references to it still resolve.</summary>
    [JsonStringEnumMemberName("retired")]
    Retired,
}
