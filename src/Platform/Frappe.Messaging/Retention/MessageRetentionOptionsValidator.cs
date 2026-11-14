using Frappe.Messaging.JetStream;
using Microsoft.Extensions.Options;

namespace Frappe.Messaging.Retention;

/// <summary>
/// Fails at startup, naming the key, on a retention that would delete what is still needed or cannot be computed:
/// the outbox keeps its rows at least a day, the inbox at least the stream MaxAge
/// (<see cref="JetStreamTopology.ModuleRetention"/>), neither more than <see cref="MessageRetentionOptions.MaxDays"/>.
/// </summary>
/// <param name="optionsName">The named options this validator checks: the module's.</param>
/// <param name="section">The configuration section they were bound from.</param>
public sealed class MessageRetentionOptionsValidator(string optionsName, string section) : IValidateOptions<MessageRetentionOptions>
{
    public ValidateOptionsResult Validate(string? name, MessageRetentionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (name != optionsName)
        {
            return ValidateOptionsResult.Skip;
        }

        var failures = new List<string>();
        var minimumInboxDays = (int)Math.Ceiling(JetStreamTopology.ModuleRetention.TotalDays);
        if (options.OutboxDays is < 1 or > MessageRetentionOptions.MaxDays)
        {
            failures.Add(
                $"Configuration '{section}:{nameof(options.OutboxDays)}' must be a whole number of days from 1 (a day) to "
                + $"{MessageRetentionOptions.MaxDays}, not '{options.OutboxDays}'.");
        }

        if (options.InboxDays < minimumInboxDays || options.InboxDays > MessageRetentionOptions.MaxDays)
        {
            failures.Add(
                $"Configuration '{section}:{nameof(options.InboxDays)}' must be a whole number of days from {minimumInboxDays} "
                + $"(the stream MaxAge) to {MessageRetentionOptions.MaxDays}, not '{options.InboxDays}': a replayed message whose "
                + "inbox record is gone would be handled again.");
        }

        if (options.BatchSize < 1)
        {
            failures.Add($"Configuration '{section}:{nameof(options.BatchSize)}' must be at least 1, not '{options.BatchSize}'.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
