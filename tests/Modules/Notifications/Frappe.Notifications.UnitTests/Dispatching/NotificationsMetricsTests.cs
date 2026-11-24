using System.Diagnostics.Metrics;
using Frappe.Notifications.Application.Dispatching;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Notifications.UnitTests.Dispatching;

/// <summary>The deliveries instrument keeps its published name, unit and description (OBS-01, OBS-02).</summary>
public sealed class NotificationsMetricsTests
{
    [Test]
    public async Task The_deliveries_counter_keeps_its_name_unit_and_description()
    {
        // Arrange
        using var provider = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var factory = provider.GetRequiredService<IMeterFactory>();
        var published = new List<Instrument>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, _) =>
        {
            if (instrument.Meter.Scope == factory)
            {
                published.Add(instrument);
            }
        };
        listener.Start();

        // Act
        _ = new NotificationsMetrics(factory);

        // Assert
        var counter = published.Single();
        using (Assert.Multiple())
        {
            await Assert.That(counter.Meter.Name).IsEqualTo("Frappe.Notifications");
            await Assert.That(counter.Name).IsEqualTo("frappe.notifications.deliveries");
            await Assert.That(counter.Unit).IsEqualTo("{delivery}");
            await Assert.That(counter.Description).IsEqualTo("Intended notification deliveries, by kind, channel and outcome.");
        }
    }
}
