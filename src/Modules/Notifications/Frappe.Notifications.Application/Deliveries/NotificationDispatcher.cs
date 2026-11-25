using System.Diagnostics;
using Frappe.Notifications.Application.Channels;
using Frappe.Notifications.Application.Telemetry;
using Frappe.Notifications.Domain.Deliveries;
using Microsoft.Extensions.Logging;

namespace Frappe.Notifications.Application.Deliveries;

/// <summary>
/// The pipeline's entry, called by consumers inside the before-claim step: resolve the recipient, gate by preference, pick the channels,
/// gate by suppression, deliver, and stage the delivery rows. It stages and never saves, so the claim's transaction commits them
/// together (ARCH-13). A transient provider fault propagates, so the message is redelivered (S14). It records what it decided in
/// <see cref="PendingDeliveries"/>, counted only once the claim committed (OBS-06), and logs the outcomes that need a reader without any address.
/// </summary>
public sealed partial class NotificationDispatcher(
    RecipientResolution resolution,
    PreferenceGate preferenceGate,
    ChannelSelection channels,
    SuppressionGate suppressionGate,
    DeliveryRecorder recorder,
    PendingDeliveries pending,
    ILogger<NotificationDispatcher> logger)
{
    /// <summary>The channel tag of an outcome no channel carried (the kind has none).</summary>
    internal const string NoChannelTag = "none";

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
        var names = channels.ChannelsFor(request.Kind).Select(channel => channel.Name).ToList();
        if (names.Count == 0)
        {
            Count(outcomes, request, NoChannelTag, DispatchOutcome.NoChannel);
            return;
        }

        foreach (var channel in names)
        {
            using var activity = NotificationsTelemetry.StartDelivery(request.Kind.Name, channel.Value);
            var target = Target(request, channel, unresolved.Key, null);
            var outcome = await recorder.AlreadyRecordedAsync(target, cancellationToken)
                ? DispatchOutcome.Duplicate
                : recorder.RecordUnresolved(target);
            Count(outcomes, request, channel.Value, outcome, activity);
        }
    }

    private async Task DispatchReachableAsync(
        NotificationRequest request, Resolution.Reachable reachable, List<DispatchOutcome> outcomes, CancellationToken cancellationToken)
    {
        var kind = request.Kind;
        var first = reachable.Contacts[0];
        if (!await preferenceGate.AllowsAsync(first, kind.Category, cancellationToken))
        {
            Count(outcomes, request, first.Contact.Channel.Value, DispatchOutcome.OptedOut);
            return;
        }

        var selected = channels.Select(kind, reachable.Contacts);
        if (selected.Count == 0)
        {
            Count(outcomes, request, first.Contact.Channel.Value, DispatchOutcome.NoChannel);
            return;
        }

        foreach (var (channel, contact) in selected)
        {
            using var activity = NotificationsTelemetry.StartDelivery(request.Kind.Name, channel.Name.Value);
            var outcome = await DeliverAsync(request, channel, contact, cancellationToken);
            Count(outcomes, request, channel.Name.Value, outcome, activity);
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

    private void Count(
        List<DispatchOutcome> outcomes, NotificationRequest request, string channel, DispatchOutcome outcome, Activity? activity = null)
    {
        outcomes.Add(outcome);
        pending.Add(request.Kind.Name, channel, outcome);
        activity?.SetTag(NotificationsTelemetry.OutcomeTag, NotificationsMetrics.OutcomeName(outcome));
        Log(request.Kind.Name, channel, outcome);
    }

    /// <summary>Logs what a reader needs to follow: the kind and the channel, never the address or the payload (S13).</summary>
    private void Log(string kind, string channel, DispatchOutcome outcome)
    {
        switch (outcome)
        {
            case DispatchOutcome.Failed:
                LogFailed(logger, kind, channel);
                break;
            case DispatchOutcome.Unresolved:
                LogUnresolved(logger, kind, channel);
                break;
            case DispatchOutcome.Suppressed:
                LogSuppressed(logger, kind, channel);
                break;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Notification {Kind} over {Channel} failed for good and is recorded as failed")]
    private static partial void LogFailed(ILogger logger, string kind, string channel);

    [LoggerMessage(
        Level = LogLevel.Information, Message = "Notification {Kind} over {Channel} has a recipient nobody resolved and is recorded as unresolved")]
    private static partial void LogUnresolved(ILogger logger, string kind, string channel);

    [LoggerMessage(Level = LogLevel.Information, Message = "Notification {Kind} over {Channel} was skipped: the address is suppressed")]
    private static partial void LogSuppressed(ILogger logger, string kind, string channel);
}
