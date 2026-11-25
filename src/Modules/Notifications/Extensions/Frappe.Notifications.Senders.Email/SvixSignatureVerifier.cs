using System.Security.Cryptography;
using System.Text;
using Frappe.Notifications.Application.ProviderEvents;
using Microsoft.Extensions.Options;

namespace Frappe.Notifications.Senders.Email;

/// <summary>
/// Verifies the Svix signature Resend signs its webhooks with: HMAC-SHA256, under the secret's decoded bytes, of
/// <c>{svix-id}.{svix-timestamp}.{raw body}</c>, compared in constant time with every <c>v1,</c> candidate of <c>svix-signature</c> (the
/// header lists several while a secret rotates). A timestamp more than <see cref="WebhookSignature.Tolerance"/> from now is refused, so a
/// captured call
/// cannot be replayed later.
/// </summary>
internal sealed class SvixSignatureVerifier
{
    private const string Version = "v1,";

    private readonly byte[] _secret;

    private readonly TimeProvider _clock;

    public SvixSignatureVerifier(IOptions<ResendWebhookOptions> options, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        _secret = TryDecodeSecret(options.Value.WebhookSecret, out var secret)
            ? secret
            : throw new InvalidOperationException($"Configuration '{ResendWebhookOptions.Section}:WebhookSecret' is not a Svix secret.");
        _clock = clock;
    }

    /// <summary>Decodes a secret as Resend shows it, with or without its <c>whsec_</c> prefix.</summary>
    public static bool TryDecodeSecret(string? value, out byte[] secret)
    {
        secret = [];
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var encoded = value.Trim();
        if (encoded.StartsWith(ResendWebhookOptions.SecretPrefix, StringComparison.Ordinal))
        {
            encoded = encoded[ResendWebhookOptions.SecretPrefix.Length..];
        }

        var buffer = new byte[encoded.Length];
        if (!Convert.TryFromBase64String(encoded, buffer, out var written) || written < ResendWebhookOptions.MinimumSecretBytes)
        {
            return false;
        }

        secret = buffer[..written];
        return true;
    }

    /// <summary>Whether the call is authentic and fresh.</summary>
    public bool IsValid(string? id, string? timestamp, string? signatures, ReadOnlySpan<byte> body)
    {
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(signatures) || !IsFresh(timestamp))
        {
            return false;
        }

        var prefix = Encoding.UTF8.GetBytes($"{id}.{timestamp}.");
        var signed = new byte[prefix.Length + body.Length];
        prefix.CopyTo(signed, 0);
        body.CopyTo(signed.AsSpan(prefix.Length));
        var expected = HMACSHA256.HashData(_secret, signed);

        var matched = false;
        foreach (var candidate in signatures.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            // Every candidate is compared, whatever the first said, so the time taken does not tell which one matched.
            matched |= candidate.StartsWith(Version, StringComparison.Ordinal) && Matches(candidate[Version.Length..], expected);
        }

        return matched;
    }

    private bool IsFresh(string? timestamp)
    {
        if (!long.TryParse(timestamp, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var seconds))
        {
            return false;
        }

        var difference = _clock.GetUtcNow().ToUnixTimeSeconds() - seconds;
        return Math.Abs(difference) <= WebhookSignature.Tolerance.TotalSeconds;
    }

    private static bool Matches(string encoded, byte[] expected)
    {
        Span<byte> actual = stackalloc byte[expected.Length + 3];
        return Convert.TryFromBase64String(encoded, actual, out var written)
            && CryptographicOperations.FixedTimeEquals(actual[..written], expected);
    }
}
