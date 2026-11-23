using System.Diagnostics;
using System.Diagnostics.Metrics;
using Frappe.Messaging;
using Frappe.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NodaTime;

namespace Frappe.Platform.UnitTests.Messaging;

/// <summary>
/// The messaging instruments: the purge counter describes every table a retention purge counts; the consumed counter has
/// one tag per outcome; the outbox gauge ages the oldest waiting message at every collection.
/// </summary>
public sealed class MessagingMetricsTests
{
    [Test]
    public async Task The_purge_counter_says_it_counts_every_retention_purge_by_module_and_table()
    {
        // Arrange
        await using var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var factory = services.GetRequiredService<IMeterFactory>();
        string? description = null;
        string? unit = null;
        long rows = 0;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Name == MessagingMetrics.PurgedMetric && instrument.Meter.Scope == factory)
            {
                description = instrument.Description;
                unit = instrument.Unit;
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) => rows += value);
        listener.Start();

        // Act
        new MessagingMetrics(factory).Purged("orders", "receipts", 3);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(description).IsEqualTo(
                "Rows a retention purge deleted: published outbox rows, inbox records and a module's own records.");
            await Assert.That(unit).IsEqualTo("{row}");
            await Assert.That(rows).IsEqualTo(3L);
        }
    }

    [Test]
    public async Task Every_consumed_outcome_has_its_own_tag()
    {
        // Act
        var tags = Enum.GetValues<ConsumedOutcome>().Select(MessagingMetrics.OutcomeTagOf).ToList();

        // Assert
        await Assert.That(tags).IsEquivalentTo(["handled", "duplicate", "failed", "dead_lettered"]);
    }

    [Test]
    public async Task An_outcome_without_an_arm_fails_instead_of_being_counted_under_a_guess()
    {
        // Act
        var failure = Assert.Throws<UnreachableException>(() => MessagingMetrics.OutcomeTagOf((ConsumedOutcome)99));

        // Assert
        await Assert.That(failure).IsNotNull();
    }

    [Test]
    public async Task The_consumed_counter_counts_one_delivery_by_consumer_and_outcome()
    {
        // Arrange
        await using var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var factory = services.GetRequiredService<IMeterFactory>();
        using var capture = new MetricCapture(factory);

        new MessagingMetrics(factory).Consumed("orders-projection", ConsumedOutcome.DeadLettered);

        // Act
        var consumed = capture.Of(MessagingMetrics.ConsumedMetric).Single();

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(consumed.Value).IsEqualTo(1d);
            await Assert.That(consumed.Tags[MessagingMetrics.ConsumerTag]).IsEqualTo("orders-projection");
            await Assert.That(consumed.Tags[MessagingMetrics.OutcomeTag]).IsEqualTo("dead_lettered");
        }
    }

    [Test]
    public async Task The_relayed_counter_counts_by_module_and_skips_empty_passes()
    {
        // Arrange
        await using var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var factory = services.GetRequiredService<IMeterFactory>();
        using var capture = new MetricCapture(factory);
        var metrics = new MessagingMetrics(factory);

        metrics.Relayed("orders", 3);
        metrics.Relayed("orders", 0);

        // Act
        var relayed = capture.Of(MessagingMetrics.RelayedMetric).Single();

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(relayed.Value).IsEqualTo(3d);
            await Assert.That(relayed.Tags[MessagingMetrics.ModuleTag]).IsEqualTo("orders");
        }
    }

    [Test]
    public async Task The_outbox_gauge_ages_the_oldest_waiting_message_per_module_and_reads_zero_when_none_waits()
    {
        // Arrange
        await using var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var factory = services.GetRequiredService<IMeterFactory>();
        using var capture = new MetricCapture(factory);
        var occurred = new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
        var time = new FakeTimeProvider(occurred.AddMinutes(2));
        var metrics = new MessagingMetrics(factory, time);

        metrics.OldestUnpublished("orders", Instant.FromDateTimeOffset(occurred));
        metrics.OldestUnpublished("billing", null);
        capture.Observe();
        time.SetUtcNow(occurred.AddMinutes(7));
        capture.Observe();

        // Act
        var ages = capture.Of(MessagingMetrics.OutboxOldestAgeMetric)
            .Select(m => ((string)m.Tags[MessagingMetrics.ModuleTag]!, m.Value))
            .ToList();

        // Assert
        await Assert.That(ages).IsEquivalentTo([("orders", 120d), ("billing", 0d), ("orders", 420d), ("billing", 0d)]);
    }

    [Test]
    public async Task Every_relay_and_consumer_failure_has_its_own_bounded_tag()
    {
        // Act
        var relay = Enum.GetValues<RelayFailure>().Select(MessagingMetrics.RelayFailureTagOf).ToList();
        var consumer = Enum.GetValues<ConsumerFailure>().Select(MessagingMetrics.ConsumerFailureTagOf).ToList();
        var unknownRelay = Assert.Throws<UnreachableException>(() => MessagingMetrics.RelayFailureTagOf((RelayFailure)99));
        var unknownConsumer = Assert.Throws<UnreachableException>(() => MessagingMetrics.ConsumerFailureTagOf((ConsumerFailure)99));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(relay).IsEquivalentTo(["rejected", "parked", "unavailable"]);
            await Assert.That(consumer).IsEquivalentTo(["subscription", "after_commit"]);
            await Assert.That(unknownRelay).IsNotNull();
            await Assert.That(unknownConsumer).IsNotNull();
        }
    }

    [Test]
    public async Task The_failure_counters_count_one_failure_by_module_or_consumer_and_its_kind()
    {
        // Arrange
        await using var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var factory = services.GetRequiredService<IMeterFactory>();
        using var capture = new MetricCapture(factory);
        var metrics = new MessagingMetrics(factory);

        // Act
        metrics.RelayFailed("orders", RelayFailure.Parked);
        metrics.ConsumerFailed("orders-projection", ConsumerFailure.Subscription);
        metrics.PurgeFailed("orders");

        // Assert
        var relay = capture.Of(MessagingMetrics.RelayFailuresMetric).Single();
        var consumer = capture.Of(MessagingMetrics.ConsumerFailuresMetric).Single();
        var purge = capture.Of(MessagingMetrics.PurgeFailuresMetric).Single();
        using (Assert.Multiple())
        {
            await Assert.That(relay.Value).IsEqualTo(1d);
            await Assert.That(relay.Tags[MessagingMetrics.ModuleTag]).IsEqualTo("orders");
            await Assert.That(relay.Tags[MessagingMetrics.OutcomeTag]).IsEqualTo("parked");
            await Assert.That(consumer.Tags[MessagingMetrics.ConsumerTag]).IsEqualTo("orders-projection");
            await Assert.That(consumer.Tags[MessagingMetrics.ReasonTag]).IsEqualTo("subscription");
            await Assert.That(purge.Value).IsEqualTo(1d);
            await Assert.That(purge.Tags[MessagingMetrics.ModuleTag]).IsEqualTo("orders");
        }
    }
}
