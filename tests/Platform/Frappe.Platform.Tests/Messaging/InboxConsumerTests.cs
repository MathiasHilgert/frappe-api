using System.Text.Json;
using Frappe.IntegrationEvents;
using Frappe.Messaging;
using Frappe.Messaging.Hosting;
using Frappe.Messaging.Inbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Frappe.Platform.Tests.Messaging;

/// <summary>
/// Which steps a consumer runs for a message. A consumer that declares its own steps (keyed by its durable name) runs
/// only those, so several modules can consume one event in one host; it may have only after-commit steps. A consumer
/// that declares none keeps the event's single handler and its after-commit steps, as before.
/// </summary>
[ClassDataSource<InboxDatabase>(Shared = SharedType.PerTestSession)]
public sealed class InboxConsumerTests(InboxDatabase database)
{
    private static IncomingMessage Message(Guid id) =>
        new(id, "frappe.test.something_happened.v1", JsonSerializer.SerializeToUtf8Bytes(new SomethingHappened("x"), JsonSerializerOptions.Web), null, null);

    private static InboxConsumer<InboxTestContext, SomethingHappened> Consumer(ServiceProvider services, string name) =>
        new(services.GetRequiredService<IServiceScopeFactory>(), name, NullLogger<InboxConsumer<InboxTestContext, SomethingHappened>>.Instance);

    private async Task<(ServiceProvider Services, StepLog Log)> ServicesAsync(Action<IServiceCollection> steps)
    {
        var options = await database.CreateAsync();
        var log = new StepLog();
        var services = new ServiceCollection().AddSingleton(log).AddScoped(_ => new InboxTestContext(options));
        steps(services);
        return (services.BuildServiceProvider(), log);
    }

    private static async Task<(int Inbox, string[] Effects)> StoredAsync(ServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<InboxTestContext>();
        return (await db.Set<InboxMessage>().CountAsync(), await db.Effects.Select(e => e.Step).ToArrayAsync());
    }

    [Test]
    public async Task A_consumer_with_only_an_after_commit_step_claims_the_message_and_runs_it_again_on_a_duplicate()
    {
        var (services, log) = await ServicesAsync(s => s.AddAfterCommitHandler<SomethingHappened, HookB>("b-cache"));
        await using var _ = services;
        var id = Guid.NewGuid();

        var first = await Consumer(services, "b-cache").ConsumeAsync(Message(id), CancellationToken.None);
        var again = await Consumer(services, "b-cache").ConsumeAsync(Message(id), CancellationToken.None);

        await Assert.That(first.Outcome).IsEqualTo(ConsumeOutcome.Processed);
        await Assert.That(again.Outcome).IsEqualTo(ConsumeOutcome.Duplicate);
        await Assert.That(log.Steps).IsEquivalentTo(new[] { "hook-b", "hook-b" });
        await Assert.That((await StoredAsync(services)).Inbox).IsEqualTo(1);
    }

    [Test]
    public async Task Two_consumers_of_one_event_in_one_host_each_run_only_their_own_steps()
    {
        var (services, log) = await ServicesAsync(s => s
            .AddIntegrationEventHandler<SomethingHappened, HandlerA>("a-projection")
            .AddAfterCommitHandler<SomethingHappened, HookA>("a-projection")
            .AddAfterCommitHandler<SomethingHappened, HookB>("b-cache"));
        await using var _ = services;
        var id = Guid.NewGuid();

        var a = await Consumer(services, "a-projection").ConsumeAsync(Message(id), CancellationToken.None);
        var afterA = log.Steps;
        var b = await Consumer(services, "b-cache").ConsumeAsync(Message(id), CancellationToken.None);

        await Assert.That(a.Outcome).IsEqualTo(ConsumeOutcome.Processed);
        await Assert.That(b.Outcome).IsEqualTo(ConsumeOutcome.Processed);
        await Assert.That(afterA).IsEquivalentTo(new[] { "handler-a", "hook-a" });
        await Assert.That(log.Steps).IsEquivalentTo(new[] { "handler-a", "hook-a", "hook-b" });
        var stored = await StoredAsync(services);
        await Assert.That(stored.Inbox).IsEqualTo(2);
        await Assert.That(stored.Effects).IsEquivalentTo(new[] { "handler-a" });
    }

    [Test]
    public async Task A_consumer_without_steps_of_its_own_still_runs_the_events_handler_and_after_commit_steps()
    {
        var (services, log) = await ServicesAsync(s => s
            .AddScoped<IIntegrationEventHandler<SomethingHappened>, HandlerA>()
            .AddScoped<IAfterCommitHandler<SomethingHappened>, HookA>());
        await using var _ = services;

        var result = await Consumer(services, "a-projection").ConsumeAsync(Message(Guid.NewGuid()), CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(ConsumeOutcome.Processed);
        await Assert.That(log.Steps).IsEquivalentTo(new[] { "handler-a", "hook-a" });
        var stored = await StoredAsync(services);
        await Assert.That(stored.Inbox).IsEqualTo(1);
        await Assert.That(stored.Effects).IsEquivalentTo(new[] { "handler-a" });
    }

    [Test]
    public async Task A_failing_handler_rolls_back_its_effect_and_the_inbox_record_and_skips_the_after_commit_steps()
    {
        var (services, log) = await ServicesAsync(s => s
            .AddIntegrationEventHandler<SomethingHappened, FailingHandler>("a-projection")
            .AddAfterCommitHandler<SomethingHappened, HookA>("a-projection"));
        await using var _ = services;

        var result = await Consumer(services, "a-projection").ConsumeAsync(Message(Guid.NewGuid()), CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(ConsumeOutcome.Failed);
        await Assert.That(log.Steps).IsEquivalentTo(new[] { "failing" });
        var stored = await StoredAsync(services);
        await Assert.That(stored.Inbox).IsEqualTo(0);
        await Assert.That(stored.Effects).IsEquivalentTo(Array.Empty<string>());
    }

    [Test]
    public async Task A_consumer_with_no_step_at_all_fails_the_message_so_it_is_retried_instead_of_acknowledged()
    {
        var (services, _) = await ServicesAsync(s => s.AddAfterCommitHandler<SomethingHappened, HookB>("b-cache"));
        await using var __ = services;

        var result = await Consumer(services, "nobody").ConsumeAsync(Message(Guid.NewGuid()), CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(ConsumeOutcome.Failed);
        await Assert.That((await StoredAsync(services)).Inbox).IsEqualTo(0);
    }

    private sealed class HandlerA(InboxTestContext db, StepLog log) : IIntegrationEventHandler<SomethingHappened>
    {
        public Task HandleAsync(SomethingHappened integrationEvent, CancellationToken cancellationToken)
        {
            log.Add("handler-a");
            db.Effects.Add(new HandledEffect { Id = Guid.NewGuid(), Step = "handler-a" });
            return Task.CompletedTask;
        }
    }

    private sealed class FailingHandler(InboxTestContext db, StepLog log) : IIntegrationEventHandler<SomethingHappened>
    {
        public async Task HandleAsync(SomethingHappened integrationEvent, CancellationToken cancellationToken)
        {
            log.Add("failing");
            db.Effects.Add(new HandledEffect { Id = Guid.NewGuid(), Step = "failing" });
            await db.SaveChangesAsync(cancellationToken);
            throw new InvalidOperationException("The handler failed after writing.");
        }
    }

    private sealed class HookA(StepLog log) : IAfterCommitHandler<SomethingHappened>
    {
        public Task HandleAsync(SomethingHappened integrationEvent, CancellationToken cancellationToken)
        {
            log.Add("hook-a");
            return Task.CompletedTask;
        }
    }

    private sealed class HookB(StepLog log) : IAfterCommitHandler<SomethingHappened>
    {
        public Task HandleAsync(SomethingHappened integrationEvent, CancellationToken cancellationToken)
        {
            log.Add("hook-b");
            return Task.CompletedTask;
        }
    }
}
