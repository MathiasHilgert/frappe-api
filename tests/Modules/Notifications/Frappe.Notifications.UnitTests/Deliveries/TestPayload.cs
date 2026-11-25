namespace Frappe.Notifications.UnitTests.Deliveries;

/// <summary>The payload of the test kinds: who to write to, by address or by reference.</summary>
internal sealed record TestPayload(string? Address, string? ReferenceId);
