namespace Frappe.Messaging.Hosting;

/// <summary>A module whose integration events this host publishes.</summary>
internal sealed record ProducerModule(string Name);
