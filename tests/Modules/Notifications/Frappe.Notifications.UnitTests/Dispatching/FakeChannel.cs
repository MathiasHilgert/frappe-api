using Frappe.Localization;
using Frappe.Notifications.Application.Channels;
using Frappe.Notifications.Application.Kinds;
using Frappe.Notifications.Domain.Deliveries;

namespace Frappe.Notifications.UnitTests.Dispatching;

/// <summary>A channel that records what it was asked to deliver and answers with a scripted outcome or a scripted fault.</summary>
internal sealed class FakeChannel(ChannelName name) : INotificationChannel
{
    public List<DispatchItem> Delivered { get; } = [];

    public ChannelOutcome Outcome { get; set; } = new ChannelOutcome.Sent(DeliveryProvider.Resend, "msg_1", LanguageTag.Create("es-AR").Value);

    public Exception? Fault { get; set; }

    public bool Accepts { get; set; } = true;

    public ChannelName Name { get; } = name;

    public bool CanDeliver(NotificationKind kind) => Accepts;

    public Task<ChannelOutcome> DeliverAsync(DispatchItem item, CancellationToken cancellationToken)
    {
        Delivered.Add(item);
        return Fault is null ? Task.FromResult(Outcome) : Task.FromException<ChannelOutcome>(Fault);
    }
}
