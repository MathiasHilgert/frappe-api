using Frappe.Application.Abstractions.Messaging;

namespace Frappe.Identity.Application.Account;

/// <summary>
/// The check behind sensitive operations: the caller's own session completed a step-up less than five minutes ago.
/// HTTP endpoints get it through <c>RequireStepUp()</c>.
/// </summary>
public sealed record HasFreshStepUp(Guid PrincipalId, Guid SessionId) : IQuery<bool>;
