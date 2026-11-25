using System.Security.Cryptography;
using System.Text;
using Frappe.Notifications.Application.Feedback;
using Frappe.Notifications.Senders.Email;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

// S6418: the values are a published documentation vector and local-only placeholders; they protect nothing.
#pragma warning disable S6418

namespace Frappe.Notifications.UnitTests.Emails;

/// <summary>Signs webhook calls the way Resend (Svix) does, so a test sends a call the verifier must accept.</summary>
internal static class WebhookSigning
{
    /// <summary>A secret as Resend shows it (24 bytes, with the prefix).</summary>
    public const string Secret = "whsec_MfKQ9r8GKYqrTwjUPD8ILPZIo2LaLaSw";

    public static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_614_265_340);

    public static FakeTimeProvider Clock() => new(Now);

    public static SvixSignatureVerifier Verifier(FakeTimeProvider clock, string secret = Secret) =>
        new(Options.Create(new ResendWebhookOptions { WebhookSecret = secret }), clock);

    /// <summary>The <c>svix-signature</c> of a call, computed independently of the verifier.</summary>
    public static string Sign(string id, string timestamp, string body, string secret = Secret)
    {
        var key = Convert.FromBase64String(secret["whsec_".Length..]);
        var hash = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes($"{id}.{timestamp}.{body}"));
        return "v1," + Convert.ToBase64String(hash);
    }

    /// <summary>A call with the three Svix headers; <paramref name="signature"/> is computed when not given.</summary>
    public static ProviderWebhook Call(string body, string id = "msg_1", string? timestamp = null, string? signature = null)
    {
        timestamp ??= Now.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        return new ProviderWebhook(
            new Dictionary<string, string>
            {
                ["Svix-Id"] = id,
                ["svix-timestamp"] = timestamp,
                ["svix-signature"] = signature ?? Sign(id, timestamp, body),
            },
            Encoding.UTF8.GetBytes(body));
    }
}
