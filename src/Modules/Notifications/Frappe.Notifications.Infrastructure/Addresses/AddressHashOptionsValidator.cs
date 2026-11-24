using Microsoft.Extensions.Options;

namespace Frappe.Notifications.Infrastructure.Addresses;

/// <summary>Fails at startup, naming the key, when the address hash keys are missing, malformed or too short (CFG-10).</summary>
public sealed class AddressHashOptionsValidator : IValidateOptions<AddressHashOptions>
{
    public ValidateOptionsResult Validate(string? name, AddressHashOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();
        if (!options.Keys.ContainsKey(options.CurrentKeyId))
        {
            failures.Add($"{AddressHashOptions.Section}:CurrentKeyId must name one of the keys in {AddressHashOptions.Section}:Keys.");
        }

        foreach (var (id, key) in options.Keys)
        {
            if (id <= 0)
            {
                failures.Add($"{AddressHashOptions.Section}:Keys:{id} must have a positive id.");
            }

            if (!IsBase64OfAtLeast(key, AddressHashOptions.MinKeyBytes))
            {
                failures.Add($"{AddressHashOptions.Section}:Keys:{id} must be base64 of at least {AddressHashOptions.MinKeyBytes} bytes.");
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    /// <summary>Whether <paramref name="key"/> decodes from base64 to at least <paramref name="minimum"/> bytes, however long it is.</summary>
    private static bool IsBase64OfAtLeast(string? key, int minimum)
    {
        var text = key ?? string.Empty;
        var bytes = new byte[(text.Length / 4 * 3) + 3];
        return Convert.TryFromBase64String(text, bytes, out var written) && written >= minimum;
    }
}
