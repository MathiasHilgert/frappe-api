namespace Frappe.Identity.Http.Sessions;

/// <summary>A completed step-up.</summary>
/// <param name="ValidUntil">Until when the session may do sensitive actions: five minutes from now, never extended by use.</param>
public sealed record StepUpResponse(DateTimeOffset ValidUntil);
