using Microsoft.Extensions.Options;

namespace Frappe.Messaging.Relay;

/// <summary>
/// Fails at startup, naming the key, on relay options that cannot work: the lease must outlive a whole pass plus one
/// publish and the settle, or a live relay could lose rows it is still publishing to another relay.
/// </summary>
/// <param name="optionsName">The named options this validator checks: the module's.</param>
/// <param name="section">The configuration section they were bound from.</param>
public sealed class OutboxRelayOptionsValidator(string optionsName, string section) : IValidateOptions<OutboxRelayOptions>
{
    public ValidateOptionsResult Validate(string? name, OutboxRelayOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (name != optionsName)
        {
            return ValidateOptionsResult.Skip;
        }

        var failures = new List<string>();
        if (options.BatchSize < 1)
        {
            failures.Add($"Configuration '{section}:{nameof(options.BatchSize)}' must be at least 1, not '{options.BatchSize}'.");
        }

        if (options.MaxAttempts < 1)
        {
            failures.Add($"Configuration '{section}:{nameof(options.MaxAttempts)}' must be at least 1, not '{options.MaxAttempts}'.");
        }

        foreach (var (key, value) in new[]
        {
            (nameof(options.PublishTimeout), options.PublishTimeout),
            (nameof(options.PollInterval), options.PollInterval),
            (nameof(options.MaxPassDuration), options.MaxPassDuration),
        })
        {
            if (value <= TimeSpan.Zero)
            {
                failures.Add($"Configuration '{section}:{key}' must be positive, not '{value}'.");
            }
        }

        if (options.LeaseDuration < options.MaxPassDuration + options.PublishTimeout + TimeSpan.FromSeconds(5))
        {
            failures.Add($"Configuration '{section}:{nameof(options.LeaseDuration)}' must be at least MaxPassDuration + PublishTimeout + 5 s, not '{options.LeaseDuration}'.");
        }

        if (options.RetryBackoff.Count == 0)
        {
            failures.Add($"Configuration '{section}:{nameof(options.RetryBackoff)}' must not be empty.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
