using Microsoft.Extensions.Options;

namespace Frappe.Notifications.Infrastructure.Retention;

/// <summary>Fails at startup, naming the key, when a retention is not a whole number of days in range or the batch is empty (CFG-01).</summary>
public sealed class NotificationsPurgeOptionsValidator : IValidateOptions<NotificationsPurgeOptions>
{
    public ValidateOptionsResult Validate(string? name, NotificationsPurgeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();
        if (options.DeliveryDays is < 1 or > NotificationsPurgeOptions.MaxDays)
        {
            failures.Add(DaysFailure(nameof(options.DeliveryDays), options.DeliveryDays));
        }

        if (options.ProviderEventDays is < 1 or > NotificationsPurgeOptions.MaxDays)
        {
            failures.Add(DaysFailure(nameof(options.ProviderEventDays), options.ProviderEventDays));
        }

        if (options.SendRequestDays is < 1 or > NotificationsPurgeOptions.MaxDays)
        {
            failures.Add(DaysFailure(nameof(options.SendRequestDays), options.SendRequestDays));
        }

        if (options.BatchSize < 1)
        {
            failures.Add(
                $"Configuration '{NotificationsPurgeOptions.Section}:{nameof(options.BatchSize)}' must be at least 1, not '{options.BatchSize}'.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static string DaysFailure(string key, int days) =>
        $"Configuration '{NotificationsPurgeOptions.Section}:{key}' must be a whole number of days from 1 to "
        + $"{NotificationsPurgeOptions.MaxDays}, not '{days}'.";
}
