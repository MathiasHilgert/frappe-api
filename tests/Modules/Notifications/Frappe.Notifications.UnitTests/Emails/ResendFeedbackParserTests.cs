using Frappe.Notifications.Application.Feedback;
using Frappe.Notifications.Channels.Email;
using Frappe.Notifications.Domain.Deliveries;
using NodaTime;

namespace Frappe.Notifications.UnitTests.Emails;

/// <summary>
/// Resend's events become the core's feedback (S4): each tracked type maps, a soft bounce never reads as permanent, an event the core does
/// not track is answered like any other, and a call that is not authentic is refused with its code and nothing else.
/// </summary>
public sealed class ResendFeedbackParserTests
{
    private static ResendFeedbackParser Parser()
    {
        var clock = WebhookSigning.Clock();
        return new ResendFeedbackParser(WebhookSigning.Verifier(clock), clock);
    }

    private static string Event(string type, string data = """{"email_id":"re_1","to":["ana@example.com"]}""") =>
        $$"""{"type":"{{type}}","created_at":"2026-11-24T10:00:00.000Z","data":{{data}}}""";

    private static ProviderFeedback? Read(string body, string id = "msg_1")
    {
        var result = Parser().Parse(WebhookSigning.Call(body, id));
        return result.Value;
    }

    [Test]
    [Arguments("email.delivered", DeliveryFeedback.Delivered)]
    [Arguments("email.delivery_delayed", DeliveryFeedback.Delayed)]
    [Arguments("email.complained", DeliveryFeedback.Complaint)]
    [Arguments("suppression.added", DeliveryFeedback.SuppressedByProvider)]
    public async Task A_tracked_event_type_maps_to_the_core_feedback_with_its_message_event_id_time_and_address(
        string type, DeliveryFeedback expected)
    {
        // Act
        var feedback = Read(Event(type), "msg_77");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(feedback).IsNotNull();
            await Assert.That(feedback!.Feedback).IsEqualTo(expected);
            await Assert.That(feedback.Provider).IsEqualTo(DeliveryProvider.Resend);
            await Assert.That(feedback.EventId).IsEqualTo("msg_77");
            await Assert.That(feedback.ProviderMessageId).IsEqualTo("re_1");
            await Assert.That(feedback.OccurredAt).IsEqualTo(Instant.FromUtc(2026, 11, 24, 10, 0));
            await Assert.That(feedback.Channel.Value).IsEqualTo("email");
            await Assert.That(feedback.Addresses).IsEquivalentTo(["ana@example.com"]);
        }
    }

    [Test]
    public async Task A_permanent_bounce_is_a_permanent_failure_and_a_transient_or_unspecified_one_is_only_a_delay()
    {
        // Act
        var hard = Read(Event("email.bounced", """{"email_id":"re_1","to":["a@example.com"],"bounce":{"type":"Permanent","subType":"General"}}"""));
        var soft = Read(Event("email.bounced", """{"email_id":"re_1","to":["a@example.com"],"bounce":{"type":"Transient"}}"""));
        var unsaid = Read(Event("email.bounced"));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(hard!.Feedback).IsEqualTo(DeliveryFeedback.PermanentFailure);
            await Assert.That(soft!.Feedback).IsEqualTo(DeliveryFeedback.Delayed);
            await Assert.That(unsaid!.Feedback).IsEqualTo(DeliveryFeedback.Delayed);
        }
    }

    [Test]
    public async Task A_provider_suppression_without_a_message_id_is_kept_for_its_address()
    {
        // Act
        var feedback = Read(Event("suppression.added", """{"to":["a@example.com"]}"""));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(feedback!.Feedback).IsEqualTo(DeliveryFeedback.SuppressedByProvider);
            await Assert.That(feedback.ProviderMessageId).IsNull();
            await Assert.That(feedback.Addresses).IsEquivalentTo(["a@example.com"]);
        }
    }

    [Test]
    public async Task An_untracked_type_or_an_unreadable_body_succeeds_with_no_feedback_so_the_provider_never_retries_it()
    {
        // Act
        var results = new[]
        {
            Read(Event("email.opened")),
            Read(Event("email.delivered", """{"to":["a@example.com"]}""")),
            Read("""{"type":"email.delivered"}"""),
            Read("[]"),
            Read("not json"),
            Read(Event("email.delivered"), new string('x', 101)),
        };

        // Assert
        await Assert.That(results.Count(r => r is not null)).IsEqualTo(0);
    }

    [Test]
    public async Task A_call_that_is_not_authentic_fails_with_the_signature_code_and_nothing_else()
    {
        // Arrange
        var forged = WebhookSigning.Call(Event("email.delivered"), signature: "v1," + Convert.ToBase64String(new byte[32]));

        // Act
        var result = Parser().Parse(forged);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.IsSuccess).IsFalse();
            await Assert.That(result.Errors.Single().Code).IsEqualTo("notifications.webhook_signature.invalid");
            await Assert.That(result.Errors.Single().Parameters).IsEmpty();
        }
    }

    [Test]
    public async Task The_feedback_and_the_call_never_print_the_address_or_the_body()
    {
        // Arrange
        var call = WebhookSigning.Call(Event("email.complained"));

        // Act
        var feedback = Read(Event("email.complained"));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(feedback!.ToString()).DoesNotContain("ana@example.com");
            await Assert.That(call.ToString()).DoesNotContain("complained");
        }
    }
}
