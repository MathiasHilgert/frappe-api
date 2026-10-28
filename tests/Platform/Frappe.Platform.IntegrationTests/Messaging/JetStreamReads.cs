using System.Text;
using Frappe.TestUtilities;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NATS.Net;

namespace Frappe.Platform.IntegrationTests.Messaging;

// Reads a scope's stream and consumers straight from NATS, the way an operator would.
internal static class JetStreamReads
{
    // A failed inbox write against a frozen database waits out the driver's timeouts before NATS redelivers.
    private static readonly TimeSpan _redeliveryTimeout = TimeSpan.FromMinutes(3);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    // Every stored message whose payload names the id.
    public static async Task<IReadOnlyList<NatsJSMsg<byte[]>>> MessagesAboutAsync(
        NatsScope scope,
        Guid id
    )
    {
        await using NatsConnection nats = Connect(scope);
        INatsJSConsumer reader = await nats.CreateJetStreamContext()
            .CreateOrderedConsumerAsync(scope.Stream, cancellationToken: Cancellation);
        List<NatsJSMsg<byte[]>> found = [];
        await foreach (
            NatsJSMsg<byte[]> message in reader.FetchNoWaitAsync<byte[]>(
                new NatsJSFetchOpts { MaxMsgs = 1000 },
                cancellationToken: Cancellation
            )
        )
        {
            if (
                Encoding
                    .UTF8.GetString(message.Data ?? [])
                    .Contains(id.ToString(), StringComparison.Ordinal)
            )
            {
                found.Add(message);
            }
        }

        return found;
    }

    public static async Task<ConsumerInfo> ConsumerAsync(NatsScope scope, string consumer)
    {
        await using NatsConnection nats = Connect(scope);
        INatsJSConsumer found = await nats.CreateJetStreamContext()
            .GetConsumerAsync(scope.Stream, consumer, Cancellation);
        return found.Info;
    }

    // Changes the stream as an operator would, behind the application's back.
    public static async Task UpdateStreamAsync(
        NatsScope scope,
        Func<StreamConfig, StreamConfig> change
    )
    {
        await using NatsConnection nats = Connect(scope);
        INatsJSContext jetStream = nats.CreateJetStreamContext();
        INatsJSStream stream = await jetStream.GetStreamAsync(
            scope.Stream,
            cancellationToken: Cancellation
        );
        await jetStream.UpdateStreamAsync(change(stream.Info.Config), Cancellation);
    }

    // Polls the consumer until the server has delivered a message again, the way an operator watching it would.
    public static async Task WaitForRedeliveryAsync(NatsScope scope, string consumer)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        timeout.CancelAfter(_redeliveryTimeout);
        using PeriodicTimer poll = new(TimeSpan.FromSeconds(1));
        while ((await ConsumerAsync(scope, consumer)).NumRedelivered == 0)
        {
            await poll.WaitForNextTickAsync(timeout.Token);
        }
    }

    // Changes a consumer as an operator would, behind the application's back.
    public static async Task UpdateConsumerAsync(
        NatsScope scope,
        string consumer,
        Action<ConsumerConfig> change
    )
    {
        await using NatsConnection nats = Connect(scope);
        INatsJSContext jetStream = nats.CreateJetStreamContext();
        ConsumerConfig config = (
            await jetStream.GetConsumerAsync(scope.Stream, consumer, Cancellation)
        )
            .Info
            .Config;
        change(config);
        await jetStream.CreateOrUpdateConsumerAsync(scope.Stream, config, Cancellation);
    }

    // Publishes straight to JetStream, as a publisher other than the application would.
    public static async Task PublishAsync(
        NatsScope scope,
        string subject,
        byte[] data,
        NatsHeaders? headers = null
    )
    {
        await using NatsConnection nats = Connect(scope);
        PubAckResponse ack = await nats.CreateJetStreamContext()
            .PublishAsync(subject, data, headers: headers, cancellationToken: Cancellation);
        ack.EnsureSuccess();
    }

    // The explicit backfill: the consumer starts again from the oldest retained event.
    public static async Task ReplayFromTheStartAsync(NatsScope scope, string consumer)
    {
        await using NatsConnection nats = Connect(scope);
        INatsJSContext jetStream = nats.CreateJetStreamContext();
        ConsumerConfig config = (
            await jetStream.GetConsumerAsync(scope.Stream, consumer, Cancellation)
        )
            .Info
            .Config;
        await jetStream.DeleteConsumerAsync(scope.Stream, consumer, Cancellation);
        config.DeliverPolicy = ConsumerConfigDeliverPolicy.All;
        await jetStream.CreateOrUpdateConsumerAsync(scope.Stream, config, Cancellation);
    }

    private static NatsConnection Connect(NatsScope scope) => new(new NatsOpts { Url = scope.Url });
}
