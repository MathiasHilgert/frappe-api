using Frappe.Notifications.Application.Channels;
using Frappe.Notifications.Domain.Deliveries;

namespace Frappe.Notifications.Application.Dispatching;

/// <summary>
/// The pipeline's entry, called by consumers inside the before-claim step: resolve the recipient, gate by preference, pick the channels,
/// gate by suppression, deliver, and stage the delivery rows. It stages and never saves, so the claim's transaction commits them
/// together (ARCH-13). A transient provider fault propagates, so the message is redelivered (S14).
/// </summary>
public sealed class NotificationDispatcher(
    RecipientResolution resolution,
    PreferenceGate preferenceGate,
    ChannelSelection channels,
    SuppressionGate suppressionGate,
    DeliveryRecorder recorder,
    NotificationsMetrics metrics)
{
    public async Task<IReadOnlyList<DispatchOutcome>> DispatchAsync(NotificationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var kind = request.Kind;
        var outcomes = new List<DispatchOutcome>();
        var resolved = await resolution.ResolveAsync(kind, request.Payload, cancellationToken);
        switch (resolved)
        {
            case Resolution.Unresolved unresolved:
                await DispatchUnresolvedAsync(request, unresolved, outcomes, cancellationToken);
                break;
            case Resolution.Reachable reachable:
                await DispatchReachableAsync(request, reachable, outcomes, cancellationToken);
                break;
            default:
                throw new System.Diagnostics.UnreachableException($"Unknown resolution {resolved}.");
        }

        return outcomes;
    }

    private async Task DispatchUnresolvedAsync(
        NotificationRequest request, Resolution.Unresolved unresolved, List<DispatchOutcome> outcomes, CancellationToken cancellationToken)
    {
        foreach (var channel in channels.ChannelsFor(request.Kind).Select(channel => channel.Name))
        {
            var target = Target(request, channel, unresolved.Key, null);
            var outcome = await recorder.AlreadyRecordedAsync(target, cancellationToken)
                ? DispatchOutcome.Duplicate
                : recorder.RecordUnresolved(target);
            Count(outcomes, request, channel, outcome);
        }
    }

    private async Task DispatchReachableAsync(
        NotificationRequest request, Resolution.Reachable reachable, List<DispatchOutcome> outcomes, CancellationToken cancellationToken)
    {
        var kind = request.Kind;
        var first = reachable.Contacts[0];
        if (!await preferenceGate.AllowsAsync(first, kind.Category, cancellationToken))
        {
            Count(outcomes, request, first.Contact.Channel, DispatchOutcome.OptedOut);
            return;
        }

        var selected = channels.Select(kind, reachable.Contacts);
        if (selected.Count == 0)
        {
            Count(outcomes, request, first.Contact.Channel, DispatchOutcome.NoChannel);
            return;
        }

        foreach (var (channel, contact) in selected)
        {
            var outcome = await DeliverAsync(request, channel, contact, cancellationToken);
            Count(outcomes, request, channel.Name, outcome);
        }
    }

    private async Task<DispatchOutcome> DeliverAsync(
        NotificationRequest request, INotificationChannel channel, ResolvedContact contact, CancellationToken cancellationToken)
    {
        if (await suppressionGate.IsSuppressedAsync(channel.Name, contact.Contact, cancellationToken))
        {
            return DispatchOutcome.Suppressed;
        }

        var target = Target(request, channel.Name, contact.Key, contact.Hash);
        if (await recorder.AlreadyRecordedAsync(target, cancellationToken))
        {
            return DispatchOutcome.Duplicate;
        }

        var key = DeliveryIdempotency.Key(request.SourceMessageId, request.Kind.Name, contact.Key, channel.Name);
        var item = new DispatchItem(request.SourceMessageId, request.Kind, request.Payload, contact.Contact, contact.Key, key);
        return await recorder.RecordAsync(target, await channel.DeliverAsync(item, cancellationToken), cancellationToken);
    }

    private static DeliveryTarget Target(NotificationRequest request, ChannelName channel, RecipientKey key, AddressHash? hash) =>
        new(request.SourceMessageId, request.Kind.Name, request.Kind.Category, channel, key, hash);

    private void Count(List<DispatchOutcome> outcomes, NotificationRequest request, ChannelName channel, DispatchOutcome outcome)
    {
        outcomes.Add(outcome);
        metrics.Delivery(request.Kind.Name, channel.Value, outcome);
    }
}
