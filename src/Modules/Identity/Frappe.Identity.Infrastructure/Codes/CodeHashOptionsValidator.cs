using Microsoft.Extensions.Options;

namespace Frappe.Identity.Infrastructure.Codes;

/// <summary>Fails at startup, naming the key, when the code hash key is missing, malformed or too short (CFG-03, CFG-10).</summary>
public sealed class CodeHashOptionsValidator : IValidateOptions<CodeHashOptions>
{
    private const string Key = $"{CodeHashOptions.Section}:Key";

    public ValidateOptionsResult Validate(string? name, CodeHashOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.Key))
        {
            return ValidateOptionsResult.Fail($"Configuration '{Key}' is required.");
        }

        return TryDecode(options.Key, out var bytes) && bytes.Length >= CodeHashOptions.MinKeyBytes
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail($"Configuration '{Key}' must be base64 of at least {CodeHashOptions.MinKeyBytes} bytes.");
    }

    /// <summary>The bytes <paramref name="text"/> spells in base64, or <see langword="false"/> when it is not base64.</summary>
    internal static bool TryDecode(string text, out byte[] bytes)
    {
        var buffer = new byte[(text.Length / 4 * 3) + 3];
        if (Convert.TryFromBase64String(text, buffer, out var written))
        {
            bytes = buffer[..written];
            return true;
        }

        bytes = [];
        return false;
    }
}
