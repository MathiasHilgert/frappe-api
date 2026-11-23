using Frappe.IntegrationEvents;

namespace Frappe.Platform.Testing.Messaging;

/// <summary>The sample integration event the messaging tests publish and consume.</summary>
[IntegrationEvent("frappe.test.something_happened.v1")]
public sealed record SomethingHappened(string What) : IIntegrationEvent;
