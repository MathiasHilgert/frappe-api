using Frappe.IntegrationEvents;

namespace Frappe.ModuleName.Contracts.AreaName;

/// <summary>
/// FILL IN: say what happened to whom and when (<see cref="OccurredAt"/>), and what a consumer may rely on.
/// </summary>
[IntegrationEvent("event_type_here")]
public sealed record UseCaseName(ContractFieldsHere) : IIntegrationEvent;
