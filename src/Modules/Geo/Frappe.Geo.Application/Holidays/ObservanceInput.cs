using Frappe.Geo.Domain.Holidays;
using Frappe.Results;
using NodaTime;
using NodaTime.Text;

namespace Frappe.Geo.Application.Holidays;

/// <summary>The parsing of the corrections' inputs, written once: absent or blank is "required", unreadable is invalid.</summary>
internal static class ObservanceInput
{
    /// <summary>An observance's date (<c>yyyy-MM-dd</c>), named <paramref name="parameter"/> in the request.</summary>
    public static Result<LocalDate> Date(string? raw, string parameter = "date")
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return GeoErrors.ParameterRequired(parameter);
        }

        return LocalDatePattern.Iso.Parse(raw) is { Success: true } parsed
            ? parsed.Value
            : ObservanceProblems.Invalid(raw, ObservanceProblems.DateInvalid);
    }

    /// <summary>An observance's name, trimmed.</summary>
    public static Result<string> Name(string? raw) =>
        string.IsNullOrWhiteSpace(raw) ? GeoErrors.ParameterRequired("name") : raw.Trim();
}
