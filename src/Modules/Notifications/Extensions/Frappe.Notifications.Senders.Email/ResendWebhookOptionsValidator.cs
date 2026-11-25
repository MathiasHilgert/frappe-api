using Microsoft.Extensions.Options;

namespace Frappe.Notifications.Senders.Email;

/// <summary>Fails the host's start, naming the key, when the webhook's signing secret is missing or is not a Svix secret (CFG-01).</summary>
public sealed class ResendWebhookOptionsValidator : IValidateOptions<ResendWebhookOptions>
{
    private const string Key = ResendWebhookOptions.Section + ":WebhookSecret";

    public ValidateOptionsResult Validate(string? name, ResendWebhookOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.WebhookSecret))
        {
            return ValidateOptionsResult.Fail($"Configuration '{Key}' must be set: the signing secret of the Resend webhook.");
        }

        return SvixSignatureVerifier.TryDecodeSecret(options.WebhookSecret, out _)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"Configuration '{Key}' must be base64 of at least {ResendWebhookOptions.MinimumSecretBytes} bytes, with or without the "
                + $"'{ResendWebhookOptions.SecretPrefix}' prefix.");
    }
}
