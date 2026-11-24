using System.Text.Json;
using Frappe.IntegrationEvents;
using Frappe.Messaging;
using Frappe.Messaging.Abstractions;
using Frappe.Messaging.Hosting;
using Frappe.Messaging.Inbox;
using Frappe.Messaging.JetStream;
using Frappe.Messaging.Relay;
using Frappe.Platform.Testing.Messaging;
using Frappe.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NATS.Client.JetStream;

namespace Frappe.Platform.IntegrationTests.Messaging;

/// <summary>
/// A JetStream consumer counts every delivery once, by how it settled it with the broker: handled, duplicate, failed
/// (redelivered) or dead_lettered, against real Postgres and NATS.
/// </summary>
[ClassDataSource<InboxDatabase, NatsServer>(Shared = [SharedType.PerTestSession, SharedType.PerTestSession])]
public sealed class JetStreamConsumerMetricsTests(InboxDatabase database, NatsServer nats)
{
    /// <summary>A readable fixed id: the same number always gives the same <see cref="Guid"/>.</summary>
    private static Guid FixedId(int number) => new(number, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    private const string Producer = "test";

    private const string EventType = "frappe.test.something_happened.v1";

    /// <summary>The consumer under test with its services, the metric capture and the stream it reads.</summary>
    private sealed record Harness(
        ServiceProvider Services,
        MetricCapture Capture,
        JetStreamConsumer<InboxTestContext, SomethingHappened> Consumer,
        INatsJSConsumer Durable,
        JetStreamNaming Naming,
        string ConsumerName) : IAsyncDisposable
    {
        /// <summary>The outcome tags counted so far, in order, all of them for this consumer.</summary>
        public async Task<IReadOnlyList<string>> OutcomesAsync()
        {
            var consumed = Capture.Of(MessagingMetrics.ConsumedMetric);
            foreach (var measurement in consumed)
            {
                await Assert.That(measurement.Tags[MessagingMetrics.ConsumerTag]).IsEqualTo(ConsumerName);
                await Assert.That(measurement.Value).IsEqualTo(1d);
            }

            return [.. consumed.Select(m => (string)m.Tags[MessagingMetrics.OutcomeTag]!)];
        }

        /// <summary>Takes the next delivery and runs it through the consumer, as its loop does.</summary>
        public async Task DeliverNextAsync()
        {
            var next = await Durable.NextAsync<byte[]>(opts: new NatsJSNextOpts { Expires = TimeSpan.FromSeconds(10) })
                ?? throw new InvalidOperationException("No delivery arrived.");
            await Consumer.HandleAsync(next, CancellationToken.None);
        }

        public async ValueTask DisposeAsync()
        {
            Capture.Dispose();
            await Services.DisposeAsync();
        }
    }

    /// <param name="steps">The consumer's steps; none means every delivery fails.</param>
    /// <param name="unreachable">Options of a database that does not exist, instead of a fresh one.</param>
    private async Task<Harness> HarnessAsync(
        Action<IServiceCollection, string> steps, Microsoft.EntityFrameworkCore.DbContextOptions<InboxTestContext>? unreachable = null)
    {
        var options = unreachable ?? await database.CreateAsync();
        var consumerName = "test-" + Guid.NewGuid().ToString("N")[..8];
        var collection = new ServiceCollection().AddMetrics().AddScoped(_ => new InboxTestContext(options));
        collection.AddJetStream("nats://localhost:4222", "tests"); // declarations are inert without a transport
        steps(collection, consumerName);
        var services = collection.BuildServiceProvider();
        var factory = services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>();
        var naming = NatsServer.FreshNaming();
        var consumer = new JetStreamConsumer<InboxTestContext, SomethingHappened>(
            nats.JetStream,
            new InboxConsumer<InboxTestContext, SomethingHappened>(
                services.GetRequiredService<IServiceScopeFactory>(),
                consumerName,
                NullLogger<InboxConsumer<InboxTestContext, SomethingHappened>>.Instance),
            Producer,
            new JetStreamConsumerOptions { MaxDeliveries = 2, Backoff = [TimeSpan.Zero] },
            new MessagingMetrics(factory),
            NullLogger<JetStreamConsumer<InboxTestContext, SomethingHappened>>.Instance,
            naming: naming);
        var capture = new MetricCapture(factory);
        var durable = await consumer.EnsureConsumerAsync(CancellationToken.None);
        return new Harness(services, capture, consumer, durable, naming, consumerName);
    }

    /// <summary>Publishes a CloudEvent with <paramref name="id"/>; without a broker message id, so a repeat is stored twice.</summary>
    private async Task PublishAsync(JetStreamNaming naming, Guid id)
    {
        var message = new OutgoingMessage(
            id,
            EventType,
            "/frappe/test",
            "application/json",
            JsonSerializer.SerializeToUtf8Bytes(new SomethingHappened("x"), JsonSerializerOptions.Web),
            NodaTime.Instant.FromUtc(2026, 11, 16, 12, 0),
            null,
            null);
        var ack = await nats.JetStream.PublishAsync(naming.Subject(EventType), message.Data.ToArray(), headers: CloudEventHeaders.From(message));
        ack.EnsureSuccess();
    }

    [Test]
    public async Task A_handled_delivery_and_its_redelivery_count_as_handled_then_duplicate()
    {
        // Arrange
        await using var harness = await HarnessAsync((s, name) => s.AddIntegrationEventHandler<SomethingHappened, Succeeding>(name));
        var id = FixedId(1);
        await PublishAsync(harness.Naming, id);
        await PublishAsync(harness.Naming, id);

        await harness.DeliverNextAsync();

        // Act
        await harness.DeliverNextAsync();

        // Assert
        await Assert.That(await harness.OutcomesAsync()).IsEquivalentTo(["handled", "duplicate"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task A_failing_delivery_counts_as_failed_until_its_last_attempt_counts_as_dead_lettered()
    {
        // Arrange
        await using var harness = await HarnessAsync((_, _) => { });
        await PublishAsync(harness.Naming, FixedId(2));

        await harness.DeliverNextAsync();

        // Act
        await harness.DeliverNextAsync();

        // Assert
        await Assert.That(await harness.OutcomesAsync())
            .IsEquivalentTo(["failed", "dead_lettered"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task A_message_that_is_not_a_Frappe_CloudEvent_counts_as_dead_lettered()
    {
        // Arrange
        await using var harness = await HarnessAsync((s, name) => s.AddIntegrationEventHandler<SomethingHappened, Succeeding>(name));
        var ack = await nats.JetStream.PublishAsync(harness.Naming.Subject(EventType), "{}"u8.ToArray());
        ack.EnsureSuccess();

        // Act
        await harness.DeliverNextAsync();

        // Assert
        await Assert.That(await harness.OutcomesAsync()).IsEquivalentTo(["dead_lettered"]);
    }

    [Test]
    public async Task A_delivery_its_database_cannot_take_counts_as_failed()
    {
        // Arrange
        // The inbox cannot be claimed (its database does not exist): the consumer's loop gives the delivery back.
        await using var harness = await HarnessAsync(
            (s, name) => s.AddIntegrationEventHandler<SomethingHappened, Succeeding>(name),
            database.Unreachable());
        await PublishAsync(harness.Naming, FixedId(3));

        await harness.Consumer.StartAsync(CancellationToken.None);
        await WaitUntil.TrueAsync(
            () => harness.Capture.Of(MessagingMetrics.ConsumedMetric).Count > 0,
            "the consumer never counted the delivery it could not take",
            TimeSpan.FromSeconds(20));

        await harness.Consumer.StopAsync(CancellationToken.None);

        // Act
        var outcomes = await harness.OutcomesAsync();

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(outcomes).IsNotEmpty();
            await Assert.That(outcomes.Distinct()).IsEquivalentTo(["failed"]);
        }
    }

    [Test]
    public async Task A_subscription_the_broker_refuses_is_counted_as_a_subscription_failure_and_retried()
    {
        // Arrange
        // A durable name with a dot and a space is refused by the broker, so the subscription never starts.
        const string badName = "bad name.x";
        var options = await database.CreateAsync();
        await using var services = new ServiceCollection().AddMetrics().AddScoped(_ => new InboxTestContext(options)).BuildServiceProvider();
        var factory = services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>();
        using var capture = new MetricCapture(factory);
        var consumer = new JetStreamConsumer<InboxTestContext, SomethingHappened>(
            nats.JetStream,
            new InboxConsumer<InboxTestContext, SomethingHappened>(
                services.GetRequiredService<IServiceScopeFactory>(),
                badName,
                NullLogger<InboxConsumer<InboxTestContext, SomethingHappened>>.Instance),
            Producer,
            new JetStreamConsumerOptions { Backoff = [TimeSpan.FromMilliseconds(10)] },
            new MessagingMetrics(factory),
            NullLogger<JetStreamConsumer<InboxTestContext, SomethingHappened>>.Instance,
            naming: NatsServer.FreshNaming());

        // Act
        await consumer.StartAsync(CancellationToken.None);
        await WaitUntil.TrueAsync(
            () => capture.Of(MessagingMetrics.ConsumerFailuresMetric).Count >= 2,
            "the consumer never counted its failed subscription twice",
            TimeSpan.FromSeconds(20));
        await consumer.StopAsync(CancellationToken.None);

        // Assert
        var failures = capture.Of(MessagingMetrics.ConsumerFailuresMetric);
        using (Assert.Multiple())
        {
            await Assert.That(failures.Select(f => (string)f.Tags[MessagingMetrics.ConsumerTag]!).Distinct()).IsEquivalentTo([badName]);
            await Assert.That(failures.Select(f => (string)f.Tags[MessagingMetrics.ReasonTag]!).Distinct()).IsEquivalentTo(["subscription"]);
        }
    }

    /// <summary>A handler that stores an effect and succeeds.</summary>
    private sealed class Succeeding(InboxTestContext db) : IIntegrationEventHandler<SomethingHappened>
    {
        public Task HandleAsync(SomethingHappened integrationEvent, CancellationToken cancellationToken)
        {
            db.Effects.Add(new HandledEffect { Id = FixedId(4), Step = "succeeding" });
            return Task.CompletedTask;
        }
    }
}
