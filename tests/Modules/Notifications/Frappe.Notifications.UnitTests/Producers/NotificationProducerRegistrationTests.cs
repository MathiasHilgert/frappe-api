using Frappe.IntegrationEvents;
using Frappe.Notifications.Application.Producers;
using Frappe.Notifications.Infrastructure;
using Frappe.Notifications.Kinds.Account;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Notifications.UnitTests.Producers;

/// <summary>What a producer plug-in declares with <c>AddNotificationProducer</c>, and what its mapper hands over (S2a).</summary>
public sealed class NotificationProducerRegistrationTests
{
    [Test]
    public async Task A_producer_declares_a_consumer_of_the_events_own_module_stream_with_its_two_steps_and_registers_the_mapper()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddNotificationProducer<OrderPlaced, OrderPlacedMapper>("notifications-orders-confirmation");

        // Assert
        var declarations = services.Select(d => d.ImplementationInstance)
            .Where(instance => instance?.GetType().Name.Contains("Declaration", StringComparison.Ordinal) == true)
            .Select(instance => instance!.ToString()!)
            .ToArray();
        using (Assert.Multiple())
        {
            await Assert.That(declarations).Count().IsEqualTo(3);
            await Assert.That(declarations[0]).Contains("ProducerModule = orders").And.Contains("ConsumerName = notifications-orders-confirmation")
                .And.Contains("Section = Notifications:Producers:notifications-orders-confirmation:Consumer");
            await Assert.That(declarations[1]).Contains("notifications-orders-confirmation");
            await Assert.That(declarations[2]).Contains("notifications-orders-confirmation");
            await Assert.That(services.Any(d => d.ServiceType == typeof(INotificationProducer<OrderPlaced>)
                && d.ImplementationType == typeof(OrderPlacedMapper))).IsTrue();
        }
    }

    [Test]
    public async Task A_consumer_name_that_is_not_notifications_own_is_refused()
    {
        // Act
        var thrown = Assert.Throws<InvalidOperationException>(
            () => new ServiceCollection().AddNotificationProducer<OrderPlaced, OrderPlacedMapper>("orders-confirmation"));

        // Assert
        await Assert.That(thrown.Message).Contains("notifications-<producer>-<purpose>");
    }

    [Test]
    public async Task A_second_mapper_for_the_same_event_is_refused_at_startup()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddNotificationProducer<OrderPlaced, OrderPlacedMapper>("notifications-orders-confirmation");

        // Act
        var thrown = Assert.Throws<InvalidOperationException>(
            () => services.AddNotificationProducer<OrderPlaced, OrderPlacedMapper>("notifications-orders-receipt"));

        // Assert
        await Assert.That(thrown.Message).Contains(nameof(OrderPlaced));
    }

    [Test]
    public async Task An_event_with_no_integration_event_type_is_refused_naming_it()
    {
        // Act
        var thrown = Assert.Throws<InvalidOperationException>(
            () => new ServiceCollection().AddNotificationProducer<Untyped, UntypedMapper>("notifications-orders-confirmation"));

        // Assert
        await Assert.That(thrown.Message).Contains(nameof(Untyped));
    }

    [Test]
    public async Task A_produced_notification_takes_the_kinds_name_and_prints_no_payload()
    {
        // Act
        var produced = ProducedNotification.Of<AccountActivationCode, AccountActivationCodePayload>(
            new AccountActivationCodePayload("ana@example.com", "es-AR", "Ana", "482913"));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(produced.Kind).IsEqualTo("account_activation_code");
            await Assert.That(produced.ToString()).DoesNotContain("ana@example.com").And.DoesNotContain("482913");
        }
    }

    /// <summary>A fact of a test-only module.</summary>
    [IntegrationEvent("frappe.orders.order_placed.v1")]
    private sealed record OrderPlaced(string Email) : IIntegrationEvent;

    /// <summary>A mapper that asks for nothing.</summary>
    private sealed class OrderPlacedMapper : INotificationProducer<OrderPlaced>
    {
        public IReadOnlyList<ProducedNotification> Map(OrderPlaced integrationEvent) => [];
    }

    /// <summary>An event with no integration event type.</summary>
    private sealed record Untyped : IIntegrationEvent;

    /// <summary>The mapper of <see cref="Untyped"/>.</summary>
    private sealed class UntypedMapper : INotificationProducer<Untyped>
    {
        public IReadOnlyList<ProducedNotification> Map(Untyped integrationEvent) => [];
    }
}
