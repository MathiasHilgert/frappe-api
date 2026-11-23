using System.Text.Json;
using Frappe.IntegrationEvents;
using Frappe.Messaging;
using Frappe.Messaging.Hosting;
using Frappe.Messaging.Inbox;
using Frappe.Platform.Testing.Messaging;
using Frappe.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Frappe.Platform.IntegrationTests.Messaging;

/// <summary>
/// Which steps a consumer runs for a message. A consumer that declares its own steps (keyed by its durable name) runs
/// only those, so several modules can consume one event in one host; it may have only after-commit steps. A consumer
/// that declares none keeps the event's single handler and its after-commit steps, as before.
/// </summary>
[ClassDataSource<InboxDatabase>(Shared = SharedType.PerTestSession)]
public sealed class InboxConsumerTests(InboxDatabase database)
{
    /// <summary>A readable fixed id: the same number always gives the same <see cref="Guid"/>.</summary>
    private static Guid FixedId(int number) => new(number, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    private static IncomingMessage Message(Guid id) =>
        new(
            id,
            "frappe.test.something_happened.v1",
            JsonSerializer.SerializeToUtf8Bytes(new SomethingHappened("x"), JsonSerializerOptions.Web),
            null,
            null);

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
        // Arrange
        var (services, log) = await ServicesAsync(s => s.AddAfterCommitHandler<SomethingHappened, HookB>("b-cache"));
        await using var _ = services;
        var id = FixedId(1);

        // Act
        var first = await Consumer(services, "b-cache").ConsumeAsync(Message(id), CancellationToken.None);
        var again = await Consumer(services, "b-cache").ConsumeAsync(Message(id), CancellationToken.None);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(first.Outcome).IsEqualTo(ConsumeOutcome.Processed);
            await Assert.That(again.Outcome).IsEqualTo(ConsumeOutcome.Duplicate);
            await Assert.That(log.Steps).IsEquivalentTo(["hook-b", "hook-b"]);
            await Assert.That((await StoredAsync(services)).Inbox).IsEqualTo(1);
        }
    }

    [Test]
    public async Task Two_consumers_of_one_event_in_one_host_each_run_only_their_own_steps()
    {
        // Arrange
        var (services, log) = await ServicesAsync(s => s
            .AddIntegrationEventHandler<SomethingHappened, HandlerA>("a-projection")
            .AddAfterCommitHandler<SomethingHappened, HookA>("a-projection")
            .AddAfterCommitHandler<SomethingHappened, HookB>("b-cache"));
        await using var _ = services;
        var id = FixedId(2);

        var a = await Consumer(services, "a-projection").ConsumeAsync(Message(id), CancellationToken.None);
        var afterA = log.Steps;

        // Act
        var b = await Consumer(services, "b-cache").ConsumeAsync(Message(id), CancellationToken.None);
        var stored = await StoredAsync(services);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(a.Outcome).IsEqualTo(ConsumeOutcome.Processed);
            await Assert.That(b.Outcome).IsEqualTo(ConsumeOutcome.Processed);
            await Assert.That(afterA).IsEquivalentTo(["handler-a", "hook-a"]);
            await Assert.That(log.Steps).IsEquivalentTo(["handler-a", "hook-a", "hook-b"]);
            await Assert.That(stored.Inbox).IsEqualTo(2);
            await Assert.That(stored.Effects).IsEquivalentTo(["handler-a"]);
        }
    }

    [Test]
    public async Task A_failing_after_commit_step_is_counted_by_consumer_and_the_message_stays_committed()
    {
        // Arrange
        var (services, _) = await ServicesAsync(s => s
            .AddMetrics()
            .AddIntegrationEventHandler<SomethingHappened, HandlerA>("a-projection")
            .AddAfterCommitHandler<SomethingHappened, FailingHook>("a-projection"));
        await using var _ = services;
        var factory = services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>();
        using var capture = new MetricCapture(factory);
        var consumer = new InboxConsumer<InboxTestContext, SomethingHappened>(
            services.GetRequiredService<IServiceScopeFactory>(),
            "a-projection",
            NullLogger<InboxConsumer<InboxTestContext, SomethingHappened>>.Instance,
            null,
            new MessagingMetrics(factory));

        // Act
        var result = await consumer.ConsumeAsync(Message(FixedId(3)), CancellationToken.None);
        var failure = capture.Of(MessagingMetrics.ConsumerFailuresMetric).Single();

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.Outcome).IsEqualTo(ConsumeOutcome.Processed);
            await Assert.That(failure.Value).IsEqualTo(1d);
            await Assert.That(failure.Tags[MessagingMetrics.ConsumerTag]).IsEqualTo("a-projection");
            await Assert.That(failure.Tags[MessagingMetrics.ReasonTag]).IsEqualTo("after_commit");
        }
    }

    [Test]
    public async Task A_consumer_without_steps_of_its_own_still_runs_the_events_handler_and_after_commit_steps()
    {
        // Arrange
        var (services, log) = await ServicesAsync(s => s
            .AddScoped<IIntegrationEventHandler<SomethingHappened>, HandlerA>()
            .AddScoped<IAfterCommitHandler<SomethingHappened>, HookA>());
        await using var _ = services;

        // Act
        var result = await Consumer(services, "a-projection").ConsumeAsync(Message(FixedId(3)), CancellationToken.None);
        var stored = await StoredAsync(services);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.Outcome).IsEqualTo(ConsumeOutcome.Processed);
            await Assert.That(log.Steps).IsEquivalentTo(["handler-a", "hook-a"]);
            await Assert.That(stored.Inbox).IsEqualTo(1);
            await Assert.That(stored.Effects).IsEquivalentTo(["handler-a"]);
        }
    }

    [Test]
    public async Task A_failing_handler_rolls_back_its_effect_and_the_inbox_record_and_skips_the_after_commit_steps()
    {
        // Arrange
        var (services, log) = await ServicesAsync(s => s
            .AddIntegrationEventHandler<SomethingHappened, FailingHandler>("a-projection")
            .AddAfterCommitHandler<SomethingHappened, HookA>("a-projection"));
        await using var _ = services;

        // Act
        var result = await Consumer(services, "a-projection").ConsumeAsync(Message(FixedId(4)), CancellationToken.None);
        var stored = await StoredAsync(services);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.Outcome).IsEqualTo(ConsumeOutcome.Failed);
            await Assert.That(log.Steps).IsEquivalentTo(["failing"]);
            await Assert.That(stored.Inbox).IsEqualTo(0);
            await Assert.That(stored.Effects).IsEquivalentTo(Array.Empty<string>());
        }
    }

    [Test]
    public async Task A_consumer_with_no_step_at_all_fails_the_message_so_it_is_retried_instead_of_acknowledged()
    {
        // Arrange
        var (services, _) = await ServicesAsync(s => s.AddAfterCommitHandler<SomethingHappened, HookB>("b-cache"));
        await using var __ = services;

        // Act
        var result = await Consumer(services, "nobody").ConsumeAsync(Message(FixedId(5)), CancellationToken.None);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.Outcome).IsEqualTo(ConsumeOutcome.Failed);
            await Assert.That((await StoredAsync(services)).Inbox).IsEqualTo(0);
        }
    }

    [Test]
    public async Task A_handler_reads_the_id_of_the_message_and_its_consumer_from_its_scope()
    {
        // Arrange
        var (services, log) = await ServicesAsync(s => s
            .AddScoped<CurrentMessage>()
            .AddIntegrationEventHandler<SomethingHappened, IdReadingHandler>("a-mailer"));
        await using var _ = services;
        var id = FixedId(6);

        // Act
        var result = await Consumer(services, "a-mailer").ConsumeAsync(Message(id), CancellationToken.None);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.Outcome).IsEqualTo(ConsumeOutcome.Processed);
            await Assert.That(log.Steps).IsEquivalentTo([$"message-{id}-a-mailer"]);
        }
    }

    [Test]
    public async Task A_before_claim_step_runs_with_no_transaction_open_and_what_it_stages_commits_with_the_claim()
    {
        // Arrange
        var (services, log) = await ServicesAsync(s => s
            .AddScoped<CurrentMessage>()
            .AddBeforeClaimHandler<SomethingHappened, EarlyStep>("a-mailer")
            .AddAfterCommitHandler<SomethingHappened, HookA>("a-mailer"));
        await using var _ = services;
        var id = FixedId(7);

        // Act
        var result = await Consumer(services, "a-mailer").ConsumeAsync(Message(id), CancellationToken.None);
        var stored = await StoredAsync(services);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.Outcome).IsEqualTo(ConsumeOutcome.Processed);
            await Assert.That(log.Steps).IsEquivalentTo([$"early-{id}-a-mailer-no-transaction", "hook-a"]);
            await Assert.That(stored.Inbox).IsEqualTo(1);
            await Assert.That(stored.Effects).IsEquivalentTo(["early"]);
        }
    }

    [Test]
    public async Task A_message_already_claimed_never_reaches_the_before_claim_step_but_still_runs_the_after_commit_steps()
    {
        // Arrange
        var (services, log) = await ServicesAsync(s => s
            .AddScoped<CurrentMessage>()
            .AddBeforeClaimHandler<SomethingHappened, EarlyStep>("a-mailer")
            .AddAfterCommitHandler<SomethingHappened, HookA>("a-mailer"));
        await using var _ = services;
        var id = FixedId(8);

        await Consumer(services, "a-mailer").ConsumeAsync(Message(id), CancellationToken.None);

        // Act
        var again = await Consumer(services, "a-mailer").ConsumeAsync(Message(id), CancellationToken.None);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(again.Outcome).IsEqualTo(ConsumeOutcome.Duplicate);
            await Assert.That(log.Steps).IsEquivalentTo([$"early-{id}-a-mailer-no-transaction", "hook-a", "hook-a"]);
            await Assert.That((await StoredAsync(services)).Effects).IsEquivalentTo(["early"]);
        }
    }

    [Test]
    public async Task A_failing_before_claim_step_claims_nothing_and_skips_the_handler_and_the_after_commit_steps()
    {
        // Arrange
        var (services, log) = await ServicesAsync(s => s
            .AddBeforeClaimHandler<SomethingHappened, FailingEarlyStep>("a-mailer")
            .AddIntegrationEventHandler<SomethingHappened, HandlerA>("a-mailer")
            .AddAfterCommitHandler<SomethingHappened, HookA>("a-mailer"));
        await using var _ = services;

        // Act
        var result = await Consumer(services, "a-mailer").ConsumeAsync(Message(FixedId(9)), CancellationToken.None);
        var stored = await StoredAsync(services);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.Outcome).IsEqualTo(ConsumeOutcome.Failed);
            await Assert.That(result.Error).Contains("The other system is down.");
            await Assert.That(log.Steps).IsEquivalentTo(["failing-early"]);
            await Assert.That(stored.Inbox).IsEqualTo(0);
            await Assert.That(stored.Effects).IsEquivalentTo(Array.Empty<string>());
        }
    }

    [Test]
    public async Task Outside_a_consumer_the_current_message_is_unknown()
    {
        // Act
        var current = new CurrentMessage();

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(current.Id).IsNull();
            await Assert.That(current.Consumer).IsNull();
        }
    }

    /// <summary>Logs the message it sees and whether a transaction is open, and stages an effect for the claim to save.</summary>
    private sealed class EarlyStep(InboxTestContext db, CurrentMessage current, StepLog log) : IBeforeClaimHandler<SomethingHappened>
    {
        public Task HandleAsync(SomethingHappened integrationEvent, CancellationToken cancellationToken)
        {
            var transaction = db.Database.CurrentTransaction is null ? "no-transaction" : "in-transaction";
            log.Add($"early-{current.Id}-{current.Consumer}-{transaction}");
            db.Effects.Add(new HandledEffect { Id = FixedId(10), Step = "early" });
            return Task.CompletedTask;
        }
    }

    /// <summary>A before-claim step that stages an effect and then fails.</summary>
    private sealed class FailingEarlyStep(InboxTestContext db, StepLog log) : IBeforeClaimHandler<SomethingHappened>
    {
        public Task HandleAsync(SomethingHappened integrationEvent, CancellationToken cancellationToken)
        {
            log.Add("failing-early");
            db.Effects.Add(new HandledEffect { Id = FixedId(11), Step = "failing-early" });
            throw new InvalidOperationException("The other system is down.");
        }
    }

    /// <summary>A handler that logs the message id and consumer it sees.</summary>
    private sealed class IdReadingHandler(CurrentMessage current, StepLog log) : IIntegrationEventHandler<SomethingHappened>
    {
        public Task HandleAsync(SomethingHappened integrationEvent, CancellationToken cancellationToken)
        {
            log.Add($"message-{current.Id}-{current.Consumer}");
            return Task.CompletedTask;
        }
    }

    /// <summary>A handler that stages an effect and succeeds.</summary>
    private sealed class HandlerA(InboxTestContext db, StepLog log) : IIntegrationEventHandler<SomethingHappened>
    {
        public Task HandleAsync(SomethingHappened integrationEvent, CancellationToken cancellationToken)
        {
            log.Add("handler-a");
            db.Effects.Add(new HandledEffect { Id = FixedId(12), Step = "handler-a" });
            return Task.CompletedTask;
        }
    }

    /// <summary>A handler that stages an effect and then fails.</summary>
    private sealed class FailingHandler(InboxTestContext db, StepLog log) : IIntegrationEventHandler<SomethingHappened>
    {
        public async Task HandleAsync(SomethingHappened integrationEvent, CancellationToken cancellationToken)
        {
            log.Add("failing");
            db.Effects.Add(new HandledEffect { Id = FixedId(13), Step = "failing" });
            await db.SaveChangesAsync(cancellationToken);
            throw new InvalidOperationException("The handler failed after writing.");
        }
    }

    /// <summary>An after-commit handler that fails.</summary>
    private sealed class FailingHook : IAfterCommitHandler<SomethingHappened>
    {
        public Task HandleAsync(SomethingHappened integrationEvent, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The cache is down.");
    }

    /// <summary>An after-commit handler that logs it ran.</summary>
    private sealed class HookA(StepLog log) : IAfterCommitHandler<SomethingHappened>
    {
        public Task HandleAsync(SomethingHappened integrationEvent, CancellationToken cancellationToken)
        {
            log.Add("hook-a");
            return Task.CompletedTask;
        }
    }

    /// <summary>An after-commit handler that logs it ran, second after HookA.</summary>
    private sealed class HookB(StepLog log) : IAfterCommitHandler<SomethingHappened>
    {
        public Task HandleAsync(SomethingHappened integrationEvent, CancellationToken cancellationToken)
        {
            log.Add("hook-b");
            return Task.CompletedTask;
        }
    }
}
