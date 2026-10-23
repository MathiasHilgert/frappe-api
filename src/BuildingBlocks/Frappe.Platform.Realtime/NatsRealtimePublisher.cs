using System.Text;
using Frappe.Application.Abstractions.Realtime;
using NATS.Client.JetStream;

namespace Frappe.Platform.Realtime;

internal sealed class NatsRealtimePublisher(INatsJSContext jetStream) : IRealtimePublisher
{
    public async Task PublishAsync(RealtimePush push, CancellationToken cancellationToken)
    {
        var subject = RealtimeSubjects.For(push.BusinessId, push.Topic);

        // MsgId lets JetStream drop redeliveries coming from the at-least-once outbox.
        var ack = await jetStream.PublishAsync(
            subject,
            Encoding.UTF8.GetBytes(push.PayloadJson),
            opts: new NatsJSPubOpts { MsgId = push.EventId.ToString() },
            cancellationToken: cancellationToken);
        ack.EnsureSuccess();
    }
}
