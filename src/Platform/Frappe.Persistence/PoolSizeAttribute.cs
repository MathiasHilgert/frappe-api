using System.ComponentModel.DataAnnotations;

namespace Frappe.Persistence;

/// <summary>
/// A connection pool size from 1 to <see cref="DatabaseOptions.MaxPoolSizeLimit"/> whose rejection names the full
/// configuration key. The generated validator ignores the <c>ErrorMessage</c> of <see cref="RangeAttribute"/>, so the
/// message is built here.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
internal sealed class PoolSizeAttribute(string configurationKey) : ValidationAttribute
{
    public override bool IsValid(object? value) => value is int and >= 1 and <= DatabaseOptions.MaxPoolSizeLimit;

    public override string FormatErrorMessage(string name) =>
        $"Configuration '{configurationKey}' must be between 1 and {DatabaseOptions.MaxPoolSizeLimit}.";
}
