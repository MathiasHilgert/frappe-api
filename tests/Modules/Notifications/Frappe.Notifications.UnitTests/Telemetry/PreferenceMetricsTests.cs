using System.Diagnostics.Metrics;
using Frappe.Notifications.Application.Telemetry;
using Frappe.Notifications.Domain.Deliveries;
using Frappe.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Notifications.UnitTests.Telemetry;

/// <summary>The unsubscribes counter keeps its published name and unit and counts by a closed category (S15, OBS-01, OBS-02, OBS-05).</summary>
public sealed class PreferenceMetricsTests
{
    [Test]
    public async Task The_unsubscribes_counter_counts_by_category()
    {
        // Arrange
        using var provider = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var factory = provider.GetRequiredService<IMeterFactory>();
        using var capture = new MetricCapture(factory);
        var metrics = new PreferenceMetrics(factory);

        // Act
        metrics.CountUnsubscribe(NotificationCategory.Marketing);

        // Assert
        var measurement = capture.Of("frappe.notifications.unsubscribes").Single();
        using (Assert.Multiple())
        {
            await Assert.That(measurement.Tags["category"]).IsEqualTo("marketing");
            await Assert.That(measurement.Value).IsEqualTo(1L);
        }
    }

    [Test]
    public async Task Every_category_has_a_named_tag_value()
    {
        // Act
        var names = Enum.GetValues<NotificationCategory>().Select(PreferenceMetrics.CategoryName).ToArray();

        // Assert
        await Assert.That(names.Distinct().Count()).IsEqualTo(Enum.GetValues<NotificationCategory>().Length);
    }
}
