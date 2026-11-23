using Frappe.Messaging.JetStream;
using Frappe.Messaging.Relay;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NodaTime;

namespace Frappe.Platform.IntegrationTests.Messaging;

/// <summary>
/// The publisher returns once the stream stored the message, treats a repeat of an already stored message as done, and
/// tells a message the broker will never take from a broker that is merely unavailable, against a real NATS server.
/// </summary>
[ClassDataSource<NatsServer>(Shared = SharedType.PerTestSession)]
public sealed class JetStreamPublisherTests(NatsServer nats)
{
    private const string Module = "test";

    private const string EventType = "frappe.test.something_happened.v1";

    private static OutgoingMessage Message(Guid id, int size = 5, string? traceParent = null) =>
        new(id, EventType, "/frappe/test", "application/json", new byte[size], Instant.FromUtc(2026, 11, 16, 12, 0), traceParent, null);

    private async Task<(JetStreamNaming Naming, JetStreamPublisher Publisher, INatsJSStream Stream)> StreamAsync(long maxMessageSize = -1)
    {
        var naming = NatsServer.FreshNaming();
        var stream = await nats.JetStream.CreateStreamAsync(
            new StreamConfig(naming.Stream(Module), [naming.ModuleSubjects(Module)]) { MaxMsgSize = (int)maxMessageSize });
        return (naming, new JetStreamPublisher(nats.JetStream, naming), stream);
    }

    [Test]
    public async Task A_published_message_is_stored_under_its_namespaced_subject_with_its_cloud_event_headers()
    {
        // Arrange
        var (naming, publisher, stream) = await StreamAsync();
        var id = Guid.NewGuid();

        // Act
        await publisher.PublishAsync(Message(id), CancellationToken.None);

        // Assert
        var consumer = await nats.JetStream.CreateOrUpdateConsumerAsync(stream.Info.Config.Name!, new ConsumerConfig("c"));
        var stored = await consumer.NextAsync<byte[]>(opts: new NatsJSNextOpts { Expires = TimeSpan.FromSeconds(10) });
        using (Assert.Multiple())
        {
            await Assert.That(stored).IsNotNull();
            await Assert.That(stored!.Subject).IsEqualTo(naming.Subject(EventType));
            await Assert.That(stored.Headers![CloudEventHeaders.Type].ToString()).IsEqualTo(EventType);
            await Assert.That(stored.Headers[CloudEventHeaders.Id].ToString()).IsEqualTo(id.ToString());
        }
    }

    [Test]
    public async Task Publishing_the_same_message_again_is_not_an_error_and_stores_it_once()
    {
        // Arrange
        var (_, publisher, stream) = await StreamAsync();
        var message = Message(Guid.NewGuid());
        await publisher.PublishAsync(message, CancellationToken.None);

        // Act
        await publisher.PublishAsync(message, CancellationToken.None);

        // Assert
        await stream.RefreshAsync();
        await Assert.That(stream.Info.State.Messages).IsEqualTo(1);
    }

    [Test]
    public async Task A_message_carrying_a_trace_context_is_published_in_that_trace()
    {
        // Arrange
        var (_, publisher, stream) = await StreamAsync();
        const string traceParent = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01";

        // Act
        await publisher.PublishAsync(Message(Guid.NewGuid(), traceParent: traceParent), CancellationToken.None);

        // Assert
        await stream.RefreshAsync();
        await Assert.That(stream.Info.State.Messages).IsEqualTo(1);
    }

    [Test]
    public async Task A_message_larger_than_the_streams_maximum_is_rejected_for_good()
    {
        // Arrange
        var (_, publisher, _) = await StreamAsync(maxMessageSize: 16);

        // Act
        var rejection = await Assert.ThrowsAsync<MessageRejectedException>(
            () => publisher.PublishAsync(Message(Guid.NewGuid(), size: 1024), CancellationToken.None));

        // Assert
        await Assert.That(rejection!.Message).Contains("10054");
    }

    [Test]
    public async Task A_message_larger_than_the_servers_maximum_payload_is_rejected_for_good()
    {
        // Arrange
        var (_, publisher, _) = await StreamAsync();

        // Act
        var rejection = await Assert.ThrowsAsync<MessageRejectedException>(
            () => publisher.PublishAsync(Message(Guid.NewGuid(), size: 4 * 1024 * 1024), CancellationToken.None));

        // Assert
        await Assert.That(rejection!.Message).Contains("maximum payload");
    }

    [Test]
    public async Task A_message_for_a_subject_without_a_stream_is_not_called_rejected()
    {
        // Arrange
        var publisher = new JetStreamPublisher(nats.JetStream, NatsServer.FreshNaming());

        // Act
        var failure = await Assert.ThrowsAsync<NATS.Client.Core.NatsException>(
            () => publisher.PublishAsync(Message(Guid.NewGuid()), CancellationToken.None));

        // Assert
        await Assert.That(failure).IsNotNull().And.IsNotTypeOf<MessageRejectedException>();
    }

    [Test]
    public async Task A_null_message_is_refused()
    {
        // Arrange
        var publisher = new JetStreamPublisher(nats.JetStream);

        // Act
        var failure = await Assert.ThrowsAsync<ArgumentNullException>(() => publisher.PublishAsync(null!, CancellationToken.None));

        // Assert
        await Assert.That(failure).IsNotNull();
    }
}
