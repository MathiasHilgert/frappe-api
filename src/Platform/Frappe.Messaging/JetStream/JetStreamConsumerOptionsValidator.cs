using Microsoft.Extensions.Options;

namespace Frappe.Messaging.JetStream;

/// <summary>
/// Fails at startup, naming the key, on consumer options too small for a safe heartbeat: AckWait at least 1 s,
/// MaxDeliveries and PrefetchCount at least 1, Backoff non-empty and MaxHandlerDuration at least AckWait.
/// </summary>
/// <param name="optionsName">The named options this validator checks: the consumer's durable name.</param>
/// <param name="section">The configuration section they were bound from.</param>
public sealed class JetStreamConsumerOptionsValidator(string optionsName, string section) : IValidateOptions<JetStreamConsumerOptions>
{
    public ValidateOptionsResult Validate(string? name, JetStreamConsumerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (name != optionsName)
        {
            return ValidateOptionsResult.Skip;
        }

        var failures = new List<string>();
        if (options.AckWait < TimeSpan.FromSeconds(1))
        {
            failures.Add($"Configuration '{section}:{nameof(options.AckWait)}' must be at least 1 s, not '{options.AckWait}'.");
        }

        if (options.MaxDeliveries < 1)
        {
            failures.Add($"Configuration '{section}:{nameof(options.MaxDeliveries)}' must be at least 1, not '{options.MaxDeliveries}'.");
        }

        if (options.PrefetchCount < 1)
        {
            failures.Add($"Configuration '{section}:{nameof(options.PrefetchCount)}' must be at least 1, not '{options.PrefetchCount}'.");
        }

        if (options.Backoff.Count == 0)
        {
            failures.Add($"Configuration '{section}:{nameof(options.Backoff)}' must not be empty.");
        }

        if (options.MaxHandlerDuration < options.AckWait)
        {
            failures.Add($"Configuration '{section}:{nameof(options.MaxHandlerDuration)}' must be at least AckWait, not '{options.MaxHandlerDuration}'.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
