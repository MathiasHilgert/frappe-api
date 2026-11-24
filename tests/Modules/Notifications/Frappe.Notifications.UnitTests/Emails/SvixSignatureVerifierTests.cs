using System.Text;
using Frappe.Notifications.Channels.Email;

namespace Frappe.Notifications.UnitTests.Emails;

/// <summary>
/// The webhook's credential is its Svix signature (S4, S12): valid, invalid and expired calls, and a vector from Svix's own documentation so
/// a wrong key derivation cannot hide behind a test that signs the same way it verifies.
/// </summary>
public sealed class SvixSignatureVerifierTests
{
    private const string Body = """{"type":"email.delivered"}""";

    private static bool Valid(SvixSignatureVerifier verifier, string id, string timestamp, string signature, string body = Body) =>
        verifier.IsValid(id, timestamp, signature, Encoding.UTF8.GetBytes(body));

    [Test]
    public async Task A_call_signed_with_the_secret_is_valid()
    {
        // Arrange
        var verifier = WebhookSigning.Verifier(WebhookSigning.Clock());
        var timestamp = WebhookSigning.Now.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);

        // Act
        var valid = Valid(verifier, "msg_1", timestamp, WebhookSigning.Sign("msg_1", timestamp, Body));

        // Assert
        await Assert.That(valid).IsTrue();
    }

    [Test]
    public async Task The_signature_of_Svix_s_documented_example_is_valid()
    {
        // Arrange: the vector of Svix's "Verifying Webhooks Manually" documentation.
        var clock = WebhookSigning.Clock();
        var verifier = WebhookSigning.Verifier(clock);

        // Act
        var valid = Valid(
            verifier, "msg_p5jXN8AQM9LWM0D4loKWxJek", "1614265330", "v1,g0hM9SsE+OTPJTGt/tmIKtSyZlE3uFJELVlNIOLJ1OE=",
            """{"test": 2432232314}""");

        // Assert
        await Assert.That(valid).IsTrue();
    }

    [Test]
    public async Task A_secret_without_the_prefix_is_the_same_secret()
    {
        // Arrange
        var verifier = WebhookSigning.Verifier(WebhookSigning.Clock(), WebhookSigning.Secret["whsec_".Length..]);
        var timestamp = WebhookSigning.Now.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);

        // Act
        var valid = Valid(verifier, "msg_1", timestamp, WebhookSigning.Sign("msg_1", timestamp, Body));

        // Assert
        await Assert.That(valid).IsTrue();
    }

    [Test]
    public async Task Any_of_several_candidates_may_match_while_a_secret_rotates()
    {
        // Arrange
        var verifier = WebhookSigning.Verifier(WebhookSigning.Clock());
        var timestamp = WebhookSigning.Now.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        var header = $"v1,{Convert.ToBase64String(new byte[32])} {WebhookSigning.Sign("msg_1", timestamp, Body)}";

        // Act
        var valid = Valid(verifier, "msg_1", timestamp, header);

        // Assert
        await Assert.That(valid).IsTrue();
    }

    [Test]
    public async Task A_tampered_body_a_wrong_id_a_wrong_secret_and_a_missing_header_are_invalid()
    {
        // Arrange
        var verifier = WebhookSigning.Verifier(WebhookSigning.Clock());
        var timestamp = WebhookSigning.Now.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        var signature = WebhookSigning.Sign("msg_1", timestamp, Body);
        var otherSecret = "whsec_" + Convert.ToBase64String(Enumerable.Repeat((byte)7, 24).ToArray());

        // Act
        var results = new[]
        {
            Valid(verifier, "msg_1", timestamp, signature, Body + " "),
            Valid(verifier, "msg_2", timestamp, signature),
            Valid(verifier, "msg_1", timestamp, WebhookSigning.Sign("msg_1", timestamp, Body, otherSecret)),
            Valid(verifier, "msg_1", timestamp, string.Empty),
            Valid(verifier, "msg_1", timestamp, signature.Replace("v1,", "v2,", StringComparison.Ordinal)),
            Valid(verifier, "msg_1", "not-a-number", signature),
            verifier.IsValid(null, timestamp, signature, Encoding.UTF8.GetBytes(Body)),
        };

        // Assert
        await Assert.That(results).IsEquivalentTo(new bool[results.Length]);
    }

    [Test]
    public async Task A_call_older_or_newer_than_five_minutes_is_invalid_and_one_inside_the_tolerance_is_valid()
    {
        // Arrange
        var clock = WebhookSigning.Clock();
        var verifier = WebhookSigning.Verifier(clock);
        var now = WebhookSigning.Now.ToUnixTimeSeconds();
        bool At(long seconds)
        {
            var timestamp = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return Valid(verifier, "msg_1", timestamp, WebhookSigning.Sign("msg_1", timestamp, Body));
        }

        // Act
        var results = new[] { At(now - 299), At(now + 299), At(now - 301), At(now + 301) };

        // Assert
        await Assert.That(results).IsEquivalentTo([true, true, false, false]);
    }
}
