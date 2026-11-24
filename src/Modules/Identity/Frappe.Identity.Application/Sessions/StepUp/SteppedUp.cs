using NodaTime;

namespace Frappe.Identity.Application.Sessions.StepUp;

/// <summary>A completed step-up.</summary>
/// <param name="ValidUntil">Until when the session may do sensitive actions; five minutes from completion, never extended by use.</param>
public sealed record SteppedUp(Instant ValidUntil);
