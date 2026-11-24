using Frappe.Messaging.Abstractions;

namespace Frappe.Messaging.Hosting;

/// <summary>A consumer registered by <see cref="MessagingDeclarations.AddIntegrationEventConsumer{TContext, TEvent}"/>.</summary>
internal sealed record ConsumerDeclaration(string Consumer, Type Event);
