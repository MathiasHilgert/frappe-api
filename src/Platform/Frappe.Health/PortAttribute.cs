using System.ComponentModel.DataAnnotations;

namespace Frappe.Health;

/// <summary>
/// A TCP port between 1 and 65535 whose rejection names the full configuration key. The generated validator ignores the
/// <c>ErrorMessage</c> of <see cref="RangeAttribute"/>, so the message is built here.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
internal sealed class PortAttribute(string configurationKey) : ValidationAttribute
{
    public override bool IsValid(object? value) => value is int port && port is >= 1 and <= 65535;

    public override string FormatErrorMessage(string name) => $"Configuration '{configurationKey}' must be a port between 1 and 65535.";
}
