using Frappe.Notifications.Application.Channels;
using Frappe.Notifications.Domain.Deliveries;
using NodaTime;

namespace Frappe.Notifications.Application.Deliveries;

/// <summary>Pipeline step 6: stages the delivery row of an outcome through the port. It never saves; the claim does (ARCH-13).</summary>
public sealed class DeliveryRecorder(IDeliveryStaging staging, TimeProvider clock)
{
    public Task<bool> AlreadyRecordedAsync(DeliveryTarget target, CancellationToken cancellationToken) =>
        staging.ExistsAsync(target, cancellationToken);

    /// <summary>Stages what the channel did and says how it counts.</summary>
    public async Task<DispatchOutcome> RecordAsync(DeliveryTarget target, ChannelOutcome outcome, CancellationToken cancellationToken)
    {
        var now = Now();
        switch (outcome)
        {
            case ChannelOutcome.Sent sent:
                var delivery = Delivery.Sent(NewId(), target, sent.Provider, sent.ProviderMessageId, sent.LocaleServed, now);
                await ApplyParkedAsync(delivery, sent.Provider, sent.ProviderMessageId, cancellationToken);
                staging.Stage(delivery);
                return DispatchOutcome.Sent;
            case ChannelOutcome.Failed failed:
                staging.Stage(Delivery.Failed(NewId(), target, failed.Reason, now));
                return DispatchOutcome.Failed;
            default:
                throw new System.Diagnostics.UnreachableException($"Unknown channel outcome {outcome}.");
        }
    }

    /// <summary>
    /// What the provider reported before this delivery was recorded (a webhook that beat the commit) applies now, oldest first and through the
    /// delivery's own guarded transitions: a refused move changes nothing (Flow 3).
    /// </summary>
    private async Task ApplyParkedAsync(Delivery delivery, DeliveryProvider provider, string providerMessageId, CancellationToken cancellationToken)
    {
        foreach (var parked in await staging.ParkedForAsync(provider, providerMessageId, cancellationToken))
        {
            _ = delivery.Apply(parked.Feedback, parked.OccurredAt);
        }
    }

    /// <summary>Stages a delivery for a reference nobody resolved (S3).</summary>
    public DispatchOutcome RecordUnresolved(DeliveryTarget target)
    {
        staging.Stage(Delivery.Unresolved(NewId(), target, Now()));
        return DispatchOutcome.Unresolved;
    }

    private Guid NewId() => Guid.CreateVersion7(clock.GetUtcNow());

    private Instant Now() => Instant.FromDateTimeOffset(clock.GetUtcNow());
}
