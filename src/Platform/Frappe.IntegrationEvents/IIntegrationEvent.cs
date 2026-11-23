namespace Frappe.IntegrationEvents;

/// <summary>
/// A fact a module publishes for other modules (and, later, other systems). It is part of the module's public
/// contract: its payload may only grow with optional fields; any other change is a new type with a new version.
/// </summary>
public interface IIntegrationEvent;
