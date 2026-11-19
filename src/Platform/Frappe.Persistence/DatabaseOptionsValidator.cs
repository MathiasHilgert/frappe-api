using Microsoft.Extensions.Options;

namespace Frappe.Persistence;

/// <summary>
/// Fails, naming each key, on database limits that cannot work: a pool of fewer than 1 connection (or more than
/// <see cref="DatabaseOptions.MaxPoolSizeLimit"/>), a command or statement timeout that is not whole seconds or whole
/// milliseconds between 1 s and 1 h, and an idle-in-transaction timeout under
/// <see cref="DatabaseOptions.MinIdleInTransactionSessionTimeout"/>. Zero, which both Npgsql and Postgres read as
/// "no limit", is refused on purpose: the point of these settings is that nothing waits forever.
/// </summary>
public sealed class DatabaseOptionsValidator : IValidateOptions<DatabaseOptions>
{
    private static readonly TimeSpan MinTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxTimeout = TimeSpan.FromHours(1);

    public ValidateOptionsResult Validate(string? name, DatabaseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();
        if (options.MaxPoolSize is < 1 or > DatabaseOptions.MaxPoolSizeLimit)
        {
            failures.Add($"Configuration '{Key(nameof(options.MaxPoolSize))}' must be between 1 and {DatabaseOptions.MaxPoolSizeLimit}, not '{options.MaxPoolSize}'.");
        }

        // Npgsql takes the command timeout in whole seconds; a fraction would be silently cut.
        if (!IsWhole(options.CommandTimeout, MinTimeout, MaxTimeout, TimeSpan.TicksPerSecond))
        {
            failures.Add($"Configuration '{Key(nameof(options.CommandTimeout))}' must be whole seconds between {MinTimeout:c} and {MaxTimeout:c}, not '{options.CommandTimeout:c}'.");
        }

        if (!IsWhole(options.StatementTimeout, MinTimeout, MaxTimeout, TimeSpan.TicksPerMillisecond))
        {
            failures.Add($"Configuration '{Key(nameof(options.StatementTimeout))}' must be whole milliseconds between {MinTimeout:c} and {MaxTimeout:c}, not '{options.StatementTimeout:c}'.");
        }

        if (!IsWhole(options.IdleInTransactionSessionTimeout, DatabaseOptions.MinIdleInTransactionSessionTimeout, MaxTimeout, TimeSpan.TicksPerMillisecond))
        {
            failures.Add($"Configuration '{Key(nameof(options.IdleInTransactionSessionTimeout))}' must be whole milliseconds between {DatabaseOptions.MinIdleInTransactionSessionTimeout:c} and {MaxTimeout:c}, not '{options.IdleInTransactionSessionTimeout:c}'.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    /// <summary>Whether <paramref name="value"/> lies in [<paramref name="min"/>, <paramref name="max"/>] and is a whole number of <paramref name="unitTicks"/>.</summary>
    private static bool IsWhole(TimeSpan value, TimeSpan min, TimeSpan max, long unitTicks) =>
        value >= min && value <= max && value.Ticks % unitTicks == 0;

    private static string Key(string property) => $"{DatabaseOptions.Section}:{property}";
}
