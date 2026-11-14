using System.Globalization;
using Frappe.Results;

namespace Frappe.Geo.Domain.Holidays;

/// <summary>A calendar year Geo keeps holidays for: 1900 to 2100.</summary>
public sealed record HolidayYear
{
    public const int Min = 1900;
    public const int Max = 2100;

    private HolidayYear(int value) => Value = value;

    public int Value { get; }

    /// <summary>A year Geo keeps, or <see cref="GeoErrorCodes.HolidayYearOutOfRange"/>.</summary>
    public static Result<HolidayYear> Create(int value) =>
        value is >= Min and <= Max
            ? new HolidayYear(value)
            : Error.InvalidValue(GeoErrorCodes.HolidayYearOutOfRange, ("value", value.ToString(CultureInfo.InvariantCulture)), ("min", Min), ("max", Max));

    /// <summary>A year as a client wrote it: digits only (<see cref="GeoErrorCodes.HolidayYearInvalid"/> otherwise), then in range.</summary>
    public static Result<HolidayYear> Parse(string? raw) =>
        int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? Create(value)
            : Error.InvalidValue(GeoErrorCodes.HolidayYearInvalid, ("value", raw), ("min", Min), ("max", Max));

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}
