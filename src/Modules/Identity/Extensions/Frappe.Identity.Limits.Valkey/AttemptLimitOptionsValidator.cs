using Microsoft.Extensions.Options;

namespace Frappe.Identity.Limits.Valkey;

/// <summary>Fails at startup, naming the full key, when a limit setting is out of range (<see cref="AttemptLimitOptions"/>, CFG-03).</summary>
public sealed class AttemptLimitOptionsValidator : IValidateOptions<AttemptLimitOptions>
{
    public ValidateOptionsResult Validate(string? name, AttemptLimitOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();
        foreach (var (flow, settings) in options.Flows)
        {
            if (settings.Limit is < 1)
            {
                failures.Add($"Configuration '{AttemptLimitOptions.Section}:{flow}:Limit' must be at least 1.");
            }

            if (settings.Window is { } window && (window <= TimeSpan.Zero || window > AttemptLimitOptions.MaxWindow))
            {
                failures.Add($"Configuration '{AttemptLimitOptions.Section}:{flow}:Window' must be above zero and at most 30 days.");
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
