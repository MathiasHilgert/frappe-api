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
