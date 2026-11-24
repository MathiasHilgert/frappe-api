using Microsoft.Extensions.Options;

namespace Frappe.Identity.Infrastructure.Retention;

/// <summary>Fails at startup, naming the key, when a retention is not a whole number of days in range or the batch is empty (CFG-01).</summary>
public sealed class IdentityRetentionOptionsValidator : IValidateOptions<IdentityRetentionOptions>
{
    public ValidateOptionsResult Validate(string? name, IdentityRetentionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        List<string> failures = [];
        CheckDays(failures, nameof(options.PendingOwnerDays), options.PendingOwnerDays);
        CheckDays(failures, nameof(options.EndedSessionDays), options.EndedSessionDays);
        CheckDays(failures, nameof(options.CodeDays), options.CodeDays);
        if (options.BatchSize < 1)
        {
            failures.Add(
                $"Configuration '{IdentityRetentionOptions.Section}:{nameof(options.BatchSize)}' must be at least 1, not '{options.BatchSize}'.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void CheckDays(List<string> failures, string key, int days)
    {
        if (days is < 1 or > IdentityRetentionOptions.MaxDays)
        {
            failures.Add(
                $"Configuration '{IdentityRetentionOptions.Section}:{key}' must be a whole number of days from 1 to "
                + $"{IdentityRetentionOptions.MaxDays}, not '{days}'.");
        }
    }
}
