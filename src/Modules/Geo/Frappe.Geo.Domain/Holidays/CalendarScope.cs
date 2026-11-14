using System.Text;
using Frappe.Results;

namespace Frappe.Geo.Domain.Holidays;

/// <summary>
/// Which calendar of a jurisdiction and year: the <see cref="General"/> one everybody observes, or a sector's own
/// (<c>banking</c>), which holds only what that sector's source publishes and is kept apart from the general one. A
/// scope is a lower-case ASCII word; which scopes exist is the country packages' business, not the core's.
/// </summary>
public sealed record CalendarScope
{
    public const int MaxLength = 16;

    private CalendarScope(string value) => Value = value;

    /// <summary>The calendar of holidays and non-working days that apply to everyone in the jurisdiction.</summary>
    public static CalendarScope General { get; } = new("general");

    public string Value { get; }

    public static Result<CalendarScope> Create(string? raw)
    {
        var trimmed = raw?.Trim() ?? string.Empty;
        return trimmed.Length is > 0 and <= MaxLength && Ascii.IsValid(trimmed) && trimmed.All(char.IsAsciiLetter)
            ? new CalendarScope(trimmed.ToLowerInvariant())
            : Error.InvalidValue(GeoErrorCodes.CalendarScopeInvalid, ("value", raw), ("max", MaxLength));
    }

    public override string ToString() => Value;
}
