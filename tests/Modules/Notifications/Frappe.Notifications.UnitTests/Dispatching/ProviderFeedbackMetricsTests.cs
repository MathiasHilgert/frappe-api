using System.Diagnostics.Metrics;
using Frappe.Notifications.Application.Dispatching;
using Frappe.Notifications.Domain.Deliveries;
using Frappe.Notifications.Domain.Suppressions;
using Frappe.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Notifications.UnitTests.Dispatching;

/// <summary>The feedback counters keep their published names, units and closed tag values (S15, OBS-01, OBS-02, OBS-05).</summary>
public sealed class ProviderFeedbackMetricsTests
{
    [Test]
    public async Task The_provider_events_counter_counts_by_a_closed_type_and_whether_the_delivery_was_found()
    {
        // Arrange
        using var provider = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var factory = provider.GetRequiredService<IMeterFactory>();
        using var capture = new MetricCapture(factory);
        var metrics = new ProviderFeedbackMetrics(factory);

        // Act
        metrics.CountProviderEvent(DeliveryFeedback.PermanentFailure, matched: true);
        metrics.CountProviderEvent(DeliveryFeedback.Delivered, matched: false);

        // Assert
        var measurements = capture.Of("frappe.notifications.provider_events");
        using (Assert.Multiple())
        {
            await Assert.That(measurements.Select(m => $"{m.Tags["type"]}:{m.Tags["matched"]}"))
                .IsEquivalentTo(["permanent_failure:True", "delivered:False"]);
            await Assert.That(measurements.Sum(m => (long)m.Value)).IsEqualTo(2L);
        }
    }

    [Test]
    public async Task The_suppressions_counter_counts_by_reason()
    {
        // Arrange
        using var provider = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var factory = provider.GetRequiredService<IMeterFactory>();
        using var capture = new MetricCapture(factory);
        var metrics = new ProviderFeedbackMetrics(factory);

        // Act
        foreach (var reason in Enum.GetValues<SuppressionReason>())
        {
            metrics.CountSuppression(reason);
        }

        // Assert
        await Assert.That(capture.Of("frappe.notifications.suppressions").Select(m => (string)m.Tags["reason"]!))
            .IsEquivalentTo(["hard_bounce", "complaint", "provider_suppression"]);
    }

    [Test]
    public async Task The_time_to_delivered_histogram_records_seconds_by_kind_and_channel()
    {
        // Arrange
        using var provider = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var factory = provider.GetRequiredService<IMeterFactory>();
        using var capture = new MetricCapture(factory);
        var metrics = new ProviderFeedbackMetrics(factory);

        // Act
        metrics.RecordTimeToDelivered("account_activation_code", "email", TimeSpan.FromMilliseconds(2500));

        // Assert
        var measurement = capture.Of("frappe.notifications.delivery.time_to_delivered").Single();
        using (Assert.Multiple())
        {
            await Assert.That(measurement.Value).IsEqualTo(2.5);
            await Assert.That(measurement.Tags["kind"]).IsEqualTo("account_activation_code");
            await Assert.That(measurement.Tags["channel"]).IsEqualTo("email");
        }
    }

    [Test]
    public async Task Every_feedback_has_a_named_type_tag()
    {
        // Act
        var names = Enum.GetValues<DeliveryFeedback>().Select(ProviderFeedbackMetrics.TypeName).ToArray();

        // Assert
        await Assert.That(names.Distinct().Count()).IsEqualTo(Enum.GetValues<DeliveryFeedback>().Length);
    }
}
