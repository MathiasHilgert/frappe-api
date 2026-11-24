using Frappe.Notifications.Application.Ports;
using Frappe.Notifications.Domain.Deliveries;
using Frappe.Notifications.Domain.ProviderEvents;
using Frappe.Notifications.Domain.Suppressions;
using Frappe.Results;
using Frappe.UseCases;
using NodaTime;

namespace Frappe.Notifications.Application.Requests.RecordProviderFeedback;

/// <summary>
/// Handles <see cref="RecordProviderFeedback"/>. Everything it stages is saved by the unit of work in one transaction: the event record
/// (its key dedupes a redelivery), the delivery's guarded move, and the suppressions. The record raises the event the counters are fed
/// from after the commit (S15).
/// </summary>
internal sealed class RecordProviderFeedbackHandler(
    IProviderEvents events,
    IDeliveries deliveries,
    ISuppressionStore suppressions,
    IAddressHasher hasher,
    IClock clock) : ICommandHandler<RecordProviderFeedback, Unit>
{
    public async Task<Result<Unit>> HandleAsync(RecordProviderFeedback command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var feedback = command.Feedback;
        if (await events.ExistsAsync(feedback.Provider, feedback.EventId, cancellationToken))
        {
            return Unit.Value;
        }

        var now = clock.GetCurrentInstant();
        var delivery = feedback.ProviderMessageId is null
            ? null
            : await deliveries.FindByProviderMessageAsync(feedback.Provider, feedback.ProviderMessageId, cancellationToken);
        if (delivery is not null)
        {
            // A move the rules refuse (a delay after a delivery, a repeat) changes nothing and is not a failure of the call.
            _ = delivery.Apply(feedback.Feedback, feedback.OccurredAt);
            events.Stage(ProviderEvent.Handled(feedback.Provider, feedback.EventId, feedback.Feedback, matched: true, now));
        }
        else if (feedback.ProviderMessageId is { } messageId)
        {
            events.Stage(ProviderEvent.Parked(feedback.Provider, feedback.EventId, messageId, feedback.Feedback, feedback.OccurredAt, now));
        }
        else
        {
            events.Stage(ProviderEvent.Handled(feedback.Provider, feedback.EventId, feedback.Feedback, matched: false, now));
        }

        await SuppressAsync(feedback, now, cancellationToken);
        return Unit.Value;
    }

    private async Task SuppressAsync(Feedback.ProviderFeedback feedback, Instant now, CancellationToken cancellationToken)
    {
        if (ReasonOf(feedback.Feedback) is not { } reason)
        {
            return;
        }

        foreach (var address in feedback.Addresses.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (await suppressions.IsSuppressedAsync(feedback.Channel, hasher.HashWithEveryKey(address), cancellationToken))
            {
                continue;
            }

            suppressions.Stage(Suppression.Add(
                feedback.Channel, hasher.Hash(address), reason, SuppressionSource.Webhook, now, feedback.Provider, feedback.EventId));
        }
    }

    /// <summary>Why an address stops: only what is permanent or the recipient's own word. A delay never suppresses.</summary>
    private static SuppressionReason? ReasonOf(DeliveryFeedback feedback) => feedback switch
    {
        DeliveryFeedback.PermanentFailure => SuppressionReason.HardBounce,
        DeliveryFeedback.Complaint => SuppressionReason.Complaint,
        DeliveryFeedback.SuppressedByProvider => SuppressionReason.ProviderSuppression,
        DeliveryFeedback.Delivered or DeliveryFeedback.Delayed => null,
        _ => throw new System.Diagnostics.UnreachableException($"Unknown feedback {feedback}."),
    };
}
