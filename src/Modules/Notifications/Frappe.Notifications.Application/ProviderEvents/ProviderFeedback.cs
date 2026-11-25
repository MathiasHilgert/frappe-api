using Frappe.Notifications.Domain.Deliveries;
using NodaTime;

namespace Frappe.Notifications.Application.ProviderEvents;

/// <summary>
/// What a provider told us about one message, in the core's terms (the seam): the output of a provider's parser and the input of
/// <c>RecordProviderFeedback</c>. The addresses it names are personal data and never print (S13).
/// </summary>
/// <param name="Provider">Who reported it.</param>
/// <param name="EventId">The provider's id of the event, unique per provider: what dedupes a redelivery.</param>
/// <param name="ProviderMessageId">The provider's id of the message it is about; none when the event names no message.</param>
/// <param name="Feedback">What the provider said.</param>
/// <param name="OccurredAt">When the provider says it happened.</param>
/// <param name="Channel">The channel the message went over, which scopes a suppression.</param>
/// <param name="Addresses">The addresses the event names, as the provider wrote them; a suppression is made for each.</param>
public sealed record ProviderFeedback(
    DeliveryProvider Provider,
    string EventId,
    string? ProviderMessageId,
    DeliveryFeedback Feedback,
    Instant OccurredAt,
    ChannelName Channel,
    IReadOnlyList<string> Addresses)
{
    public override string ToString() =>
        $"ProviderFeedback {{ Provider = {Provider}, EventId = {EventId}, Feedback = {Feedback}, Addresses = *** }}";
}
