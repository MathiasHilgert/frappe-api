using System.Globalization;
using Frappe.Geo.Domain.BusinessDays;
using Frappe.Results;
using NodaTime;
using NodaTime.Text;

namespace Frappe.Geo.Application.BusinessDays;

/// <summary>The parsing of the business-day queries' inputs, written once.</summary>
internal static class BusinessDayInput
{
    /// <summary>A date (<c>YYYY-MM-DD</c>) named <paramref name="parameter"/> in the request.</summary>
    public static Result<LocalDate> Date(string? raw, string parameter)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return GeoErrors.ParameterRequired(parameter);
        }

        return LocalDatePattern.Iso.Parse(raw) is { Success: true } parsed ? parsed.Value : GeoErrors.DateInvalidFormat(raw);
    }

    public static Result<BusinessDayPolicy> Policy(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return GeoErrors.ParameterRequired("policy");
        }

        return BusinessDayPolicies.TryParse(raw, out var policy) ? policy : GeoErrors.BusinessDayPolicyInvalid(raw);
    }

    /// <summary>A count of days from 1 to <paramref name="max"/>: digits (invalid otherwise), then in range.</summary>
    public static Result<int> Count(string? raw, int max)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return GeoErrors.ParameterRequired("count");
        }

        if (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
        {
            return GeoErrors.BusinessDayCountInvalid(raw, max);
        }

        return value is >= 1 && value <= max ? value : GeoErrors.BusinessDayCountOutOfRange(raw, max);
    }
}
