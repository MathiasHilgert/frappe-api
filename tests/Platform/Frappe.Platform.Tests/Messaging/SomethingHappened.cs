using Frappe.IntegrationEvents;

namespace Frappe.Platform.Tests.Messaging;

[IntegrationEvent("frappe.test.something_happened.v1")]
public sealed record SomethingHappened(string What) : IIntegrationEvent;
