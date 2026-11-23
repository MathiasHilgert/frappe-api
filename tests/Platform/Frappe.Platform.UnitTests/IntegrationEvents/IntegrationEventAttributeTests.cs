using System.Reflection;
using Frappe.IntegrationEvents;

namespace Frappe.Platform.UnitTests.IntegrationEvents;

/// <summary>The stable CloudEvents type an integration event declares.</summary>
public sealed class IntegrationEventAttributeTests
{
    /// <summary>An event class that declares its type.</summary>
    [IntegrationEvent("frappe.geo.holidays_changed.v1")]
    private sealed class HolidaysChanged;

    [Test]
    public async Task An_event_class_declares_its_stable_type()
    {
        // Act
        var attribute = typeof(HolidaysChanged).GetCustomAttribute<IntegrationEventAttribute>();

        // Assert
        await Assert.That(attribute?.Type).IsEqualTo("frappe.geo.holidays_changed.v1");
    }
}
