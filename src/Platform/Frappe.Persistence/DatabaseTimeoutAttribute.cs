using System.ComponentModel.DataAnnotations;

namespace Frappe.Persistence;

/// <summary>
/// A timeout between <c>minSeconds</c> and one hour, in whole <see cref="TimeoutUnit"/>s, whose rejection names the full
/// configuration key. Zero, which Npgsql and Postgres read as "no limit", is refused on purpose: the point of these settings
/// is that nothing waits forever.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
internal sealed class DatabaseTimeoutAttribute(string configurationKey, int minSeconds, TimeoutUnit unit) : ValidationAttribute
{
    private static readonly TimeSpan Max = TimeSpan.FromHours(1);

    public override bool IsValid(object? value) =>
        value is TimeSpan timeout
        && timeout >= TimeSpan.FromSeconds(minSeconds)
        && timeout <= Max
        && timeout.Ticks % (unit == TimeoutUnit.Seconds ? TimeSpan.TicksPerSecond : TimeSpan.TicksPerMillisecond) == 0;

    public override string FormatErrorMessage(string name) =>
        $"Configuration '{configurationKey}' must be whole {unit.ToString().ToLowerInvariant()} "
        + $"between {TimeSpan.FromSeconds(minSeconds):c} and {Max:c}.";
}
