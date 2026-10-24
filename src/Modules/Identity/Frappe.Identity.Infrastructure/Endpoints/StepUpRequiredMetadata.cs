namespace Frappe.Identity.Infrastructure.Endpoints;

/// <summary>Marks an endpoint guarded by <c>RequireStepUp()</c>, so the step-up policy can be read from the route table.</summary>
internal sealed class StepUpRequiredMetadata;
