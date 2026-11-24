using System.Globalization;
using System.Text.Json;
using Frappe.Notifications.Application.Feedback;
using Frappe.Notifications.Application.Ports;
using Frappe.Notifications.Domain.Deliveries;
using Frappe.Results;
using NodaTime;

namespace Frappe.Notifications.Channels.Email;

/// <summary>
/// Authenticates a Resend webhook (<see cref="SvixSignatureVerifier"/>) and reads its event into a <see cref="ProviderFeedback"/> (the
/// parser registered as <see cref="ProviderFeedbackParsers.Resend"/>). The event id is the call's <c>svix-id</c>, which Svix repeats on every
/// redelivery. Resend's shapes are read tolerantly: an event type the core does not track, a body that is not the shape expected or an id too
/// long to store is answered like any other call (no feedback), never an error, so the provider does not retry what could never be applied.
/// The body holds addresses (S13) and is never logged.
/// </summary>
internal sealed class ResendFeedbackParser(SvixSignatureVerifier verifier, TimeProvider clock) : IProviderFeedbackParser
{
    /// <summary>The channel every Resend event is about.</summary>
    private const int MessageIdLength = 100;

    internal static readonly ChannelName EmailChannel = ChannelName.Create("email").Value;

    public Result<ProviderFeedback?> Parse(ProviderWebhook webhook)
    {
        ArgumentNullException.ThrowIfNull(webhook);
        var id = webhook.Header("svix-id");
        var timestamp = webhook.Header("svix-timestamp");
        if (!verifier.IsValid(id, timestamp, webhook.Header("svix-signature"), webhook.Body.Span))
        {
            return WebhookSignature.Invalid();
        }

        if (id!.Length > Domain.ProviderEvents.ProviderEvent.EventIdLength)
        {
            return Result.Success<ProviderFeedback?>(null);
        }

        try
        {
            using var document = JsonDocument.Parse(webhook.Body);
            return Result.Success(Read(id, document.RootElement, timestamp!));
        }
        catch (JsonException)
        {
            return Result.Success<ProviderFeedback?>(null);
        }
    }

    private ProviderFeedback? Read(string eventId, JsonElement root, string timestamp)
    {
        if (root.ValueKind != JsonValueKind.Object || Text(root, "type") is not { } type || !root.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var messageId = Text(data, "email_id");
        if (messageId is { Length: > MessageIdLength })
        {
            return null;
        }

        var feedback = Map(type, data);
        if (feedback is null || (messageId is null && feedback != DeliveryFeedback.SuppressedByProvider))
        {
            return null;
        }

        return new ProviderFeedback(
            DeliveryProvider.Resend, eventId, messageId, feedback.Value, OccurredAt(root, timestamp), EmailChannel, Addresses(data));
    }

    /// <summary>
    /// The core's feedback for a Resend event type. A bounce is permanent only when Resend says so (<c>data.bounce.type</c> is
    /// <c>Permanent</c>); a transient or undetermined one is the provider still trying, so it is a delay and never suppresses.
    /// </summary>
    private static DeliveryFeedback? Map(string type, JsonElement data) => type switch
    {
        "email.delivered" => DeliveryFeedback.Delivered,
        "email.delivery_delayed" => DeliveryFeedback.Delayed,
        "email.complained" => DeliveryFeedback.Complaint,
        "email.bounced" => IsPermanent(data) ? DeliveryFeedback.PermanentFailure : DeliveryFeedback.Delayed,
        "suppression.added" or "email.suppressed" => DeliveryFeedback.SuppressedByProvider,
        _ => null,
    };

    private static bool IsPermanent(JsonElement data) =>
        data.TryGetProperty("bounce", out var bounce) && bounce.ValueKind == JsonValueKind.Object
        && string.Equals(Text(bounce, "type"), "Permanent", StringComparison.OrdinalIgnoreCase);

    private Instant OccurredAt(JsonElement root, string timestamp)
    {
        if (Text(root, "created_at") is { } created
            && DateTimeOffset.TryParse(created, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return Instant.FromDateTimeOffset(parsed);
        }

        // The signed timestamp is fresh (the verifier checked it), so it is a fair stand-in for a body that does not say.
        return long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            ? Instant.FromUnixTimeSeconds(seconds)
            : Instant.FromDateTimeOffset(clock.GetUtcNow());
    }

    private static List<string> Addresses(JsonElement data)
    {
        var addresses = new List<string>();
        if (data.TryGetProperty("to", out var to) && to.ValueKind == JsonValueKind.Array)
        {
            addresses.AddRange(to.EnumerateArray().Where(a => a.ValueKind == JsonValueKind.String).Select(a => a.GetString()!)
                .Where(a => !string.IsNullOrWhiteSpace(a)));
        }
        else if (Text(data, "email") is { } single && !string.IsNullOrWhiteSpace(single))
        {
            addresses.Add(single);
        }

        return addresses;
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
