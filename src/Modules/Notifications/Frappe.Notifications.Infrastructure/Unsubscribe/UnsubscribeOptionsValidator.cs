using Microsoft.Extensions.Options;

namespace Frappe.Notifications.Infrastructure.Unsubscribe;

/// <summary>Fails at startup, naming the key, when the unsubscribe signing keys are missing, malformed or too short (CFG-10).</summary>
public sealed class UnsubscribeOptionsValidator : IValidateOptions<UnsubscribeOptions>
{
    public ValidateOptionsResult Validate(string? name, UnsubscribeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();
        if (options.CurrentKeyId <= 0)
        {
            failures.Add($"{UnsubscribeOptions.Section}:CurrentKeyId must be positive.");
        }

        if (!IsBase64OfAtLeast(options.SigningKey))
        {
            failures.Add($"{UnsubscribeOptions.Section}:SigningKey must be set to base64 of at least {UnsubscribeOptions.MinKeyBytes} bytes.");
        }

        foreach (var (id, key) in options.RetiredKeys)
        {
            if (id <= 0 || id == options.CurrentKeyId)
            {
                failures.Add($"{UnsubscribeOptions.Section}:RetiredKeys:{id} must have a positive id other than the current one.");
            }

            if (!IsBase64OfAtLeast(key))
            {
                failures.Add($"{UnsubscribeOptions.Section}:RetiredKeys:{id} must be base64 of at least {UnsubscribeOptions.MinKeyBytes} bytes.");
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    /// <summary>Whether <paramref name="key"/> decodes from base64 to at least the minimum, however long it is.</summary>
    internal static bool IsBase64OfAtLeast(string? key)
    {
        var text = key ?? string.Empty;
        var bytes = new byte[(text.Length / 4 * 3) + 3];
        return Convert.TryFromBase64String(text, bytes, out var written) && written >= UnsubscribeOptions.MinKeyBytes;
    }
}
