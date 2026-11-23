namespace Frappe.Messaging.Hosting;

/// <summary>A consumer registered by <see cref="MessagingRegistration.AddIntegrationEventConsumer{TContext, TEvent}"/>.</summary>
internal sealed record ConsumerDeclaration(string Consumer, Type Event);
