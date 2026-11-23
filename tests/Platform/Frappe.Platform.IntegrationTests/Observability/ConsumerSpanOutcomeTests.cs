using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Frappe.IntegrationEvents;
using Frappe.Messaging;
using Frappe.Messaging.Hosting;
using Frappe.Messaging.Inbox;
using Frappe.Platform.IntegrationTests.Messaging;
using Frappe.Platform.Testing.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Frappe.Platform.IntegrationTests.Observability;

/// <summary>
/// The span of a consumed message carries <c>frappe.outcome</c> in the vocabulary of the consumed metric (OBS-11):
/// <c>handled</c>, <c>duplicate</c>, <c>failed</c> or <c>dead_lettered</c>, so a trace and a panel join by outcome.
/// </summary>
[ClassDataSource<InboxDatabase>(Shared = SharedType.PerTestSession)]
public sealed class ConsumerSpanOutcomeTests(InboxDatabase database)
{
    /// <summary>A readable fixed id: the same number always gives the same <see cref="Guid"/>.</summary>
    private static Guid FixedId(int number) => new(number, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    private static IncomingMessage Message(Guid id, byte[]? data = null) =>
        new(
            id,
            "frappe.test.something_happened.v1",
            data ?? JsonSerializer.SerializeToUtf8Bytes(new SomethingHappened("x"), JsonSerializerOptions.Web),
            null,
            null);

    private async Task<string[]> OutcomesOfAsync(
        Func<InboxConsumer<InboxTestContext, SomethingHappened>, Task> consume,
        Func<string, IServiceCollection, IServiceCollection> steps)
    {
        var consumerName = $"span-probe-{Guid.NewGuid():N}";
        var options = await database.CreateAsync();
        var services = new ServiceCollection().AddSingleton(new StepLog()).AddScoped(_ => new InboxTestContext(options));
        steps(consumerName, services);
        await using var provider = services.BuildServiceProvider();
        var spans = new ConcurrentBag<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == MessagingTelemetry.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = spans.Add,
        };
        ActivitySource.AddActivityListener(listener);

        await consume(new InboxConsumer<InboxTestContext, SomethingHappened>(
            provider.GetRequiredService<IServiceScopeFactory>(),
            consumerName,
            NullLogger<InboxConsumer<InboxTestContext, SomethingHappened>>.Instance));

        return [.. spans
            .Where(s => s.GetTagItem("messaging.consumer.group.name") as string == consumerName)
            .OrderBy(s => s.StartTimeUtc)
            .Select(s => s.GetTagItem("frappe.outcome") as string ?? "none")];
    }

    [Test]
    public async Task A_handled_message_is_tagged_handled()
    {
        // Act
        var outcomes = await OutcomesOfAsync(
            c => c.ConsumeAsync(Message(FixedId(1)), CancellationToken.None),
            (name, s) => s.AddIntegrationEventHandler<SomethingHappened, OkHandler>(name));

        // Assert
        await Assert.That(outcomes).IsEquivalentTo(["handled"]);
    }

    [Test]
    public async Task A_message_the_consumer_already_claimed_is_tagged_duplicate()
    {
        // Arrange
        var id = FixedId(2);

        // Act
        var outcomes = await OutcomesOfAsync(
            async c =>
            {
                await c.ConsumeAsync(Message(id), CancellationToken.None);
                await c.ConsumeAsync(Message(id), CancellationToken.None);
            },
            (name, s) => s.AddIntegrationEventHandler<SomethingHappened, OkHandler>(name));

        // Assert
        await Assert.That(outcomes).IsEquivalentTo(["handled", "duplicate"]);
    }

    [Test]
    public async Task A_failing_handler_is_tagged_failed()
    {
        // Act
        var outcomes = await OutcomesOfAsync(
            c => c.ConsumeAsync(Message(FixedId(3)), CancellationToken.None),
            (name, s) => s.AddIntegrationEventHandler<SomethingHappened, FailingHandler>(name));

        // Assert
        await Assert.That(outcomes).IsEquivalentTo(["failed"]);
    }

    [Test]
    public async Task An_unreadable_payload_is_tagged_dead_lettered_because_that_is_where_it_goes()
    {
        // Act
        var outcomes = await OutcomesOfAsync(
            c => c.ConsumeAsync(Message(FixedId(4), "not json"u8.ToArray()), CancellationToken.None),
            (name, s) => s.AddIntegrationEventHandler<SomethingHappened, OkHandler>(name));

        // Assert
        await Assert.That(outcomes).IsEquivalentTo(["dead_lettered"]);
    }

    /// <summary>A handler that succeeds.</summary>
    private sealed class OkHandler : IIntegrationEventHandler<SomethingHappened>
    {
        public Task HandleAsync(SomethingHappened integrationEvent, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>A handler that always fails.</summary>
    private sealed class FailingHandler : IIntegrationEventHandler<SomethingHappened>
    {
        public Task HandleAsync(SomethingHappened integrationEvent, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The handler failed.");
    }
}
