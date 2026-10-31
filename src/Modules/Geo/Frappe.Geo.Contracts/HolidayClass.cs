using System.Text.Json.Serialization;

namespace Frappe.Geo.Contracts;

/// <summary>Whether a public holiday is a paid day off or a day each employer decides to work.</summary>
public enum HolidayClass
{
    /// <summary>A paid public holiday (in Argentina, a feriado).</summary>
    [JsonStringEnumMemberName("holiday")]
    Holiday,

    /// <summary>A day the employer decides whether to work (in Argentina, a día no laborable).</summary>
    [JsonStringEnumMemberName("non_working_day")]
    NonWorkingDay,
}
