using System.ComponentModel.DataAnnotations;

namespace Frappe.Identity.Infrastructure.Credentials;

/// <summary>
/// A whole number, or a <see cref="TimeSpan"/> counted in seconds, between <c>min</c> and <c>max</c> inclusive, whose rejection names the
/// full configuration key (CFG-03: the generated validator ignores the message of <see cref="RangeAttribute"/>).
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
internal sealed class OptionRangeAttribute(string configurationKey, double min, double max) : ValidationAttribute
{
    public override bool IsValid(object? value) => value switch
    {
        int number => number >= min && number <= max,
        TimeSpan span => span.TotalSeconds >= min && span.TotalSeconds <= max,
        _ => false,
    };

    public override string FormatErrorMessage(string name) =>
        $"Configuration '{configurationKey}' must be between {min.ToString(System.Globalization.CultureInfo.InvariantCulture)} and "
        + $"{max.ToString(System.Globalization.CultureInfo.InvariantCulture)} (seconds for a duration).";
}
