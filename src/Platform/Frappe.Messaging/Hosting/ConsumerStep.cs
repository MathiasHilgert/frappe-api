namespace Frappe.Messaging.Hosting;

/// <summary>A step registered for a consumer by its durable name.</summary>
internal sealed record ConsumerStep(string Consumer, Type Event, Type Step);
