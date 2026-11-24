using Frappe.Notifications.Application.Channels;
using Frappe.Notifications.Application.Ports;
using Frappe.Notifications.Domain.Deliveries;
using NodaTime;

namespace Frappe.Notifications.Application.Dispatching;

/// <summary>Pipeline step 6: stages the delivery row of an outcome through the port. It never saves; the claim does (ARCH-13).</summary>
public sealed class DeliveryRecorder(IDeliveryStaging staging, TimeProvider clock)
{
    public Task<bool> AlreadyRecordedAsync(DeliveryTarget target, CancellationToken cancellationToken) =>
        staging.ExistsAsync(target, cancellationToken);

    /// <summary>Stages what the channel did and says how it counts.</summary>
    public DispatchOutcome Record(DeliveryTarget target, ChannelOutcome outcome)
    {
        var now = Now();
        switch (outcome)
        {
            case ChannelOutcome.Sent sent:
                staging.Stage(Delivery.Sent(NewId(), target, sent.Provider, sent.ProviderMessageId, sent.LocaleServed, now));
                return DispatchOutcome.Sent;
            case ChannelOutcome.Failed failed:
                staging.Stage(Delivery.Failed(NewId(), target, failed.Reason, now));
                return DispatchOutcome.Failed;
            default:
                throw new System.Diagnostics.UnreachableException($"Unknown channel outcome {outcome}.");
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
