using Frappe.Notifications.Application.Channels;
using Frappe.Notifications.Domain.Deliveries;
using Frappe.Notifications.Senders.Email;

namespace Frappe.Notifications.UnitTests.Emails;

/// <summary>An <see cref="IEmailSender"/> that keeps what it was given and answers that the provider took it.</summary>
internal sealed class RecordingSender : IEmailSender
{
    public List<RenderedEmail> Sent { get; } = [];

    public ChannelOutcome Outcome { get; set; } = new ChannelOutcome.Sent(DeliveryProvider.Resend, "msg_1", null);

    public Task<ChannelOutcome> SendAsync(RenderedEmail email, CancellationToken cancellationToken)
    {
        Sent.Add(email);
        return Task.FromResult(Outcome);
    }
}
