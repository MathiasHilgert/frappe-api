using Frappe.Application.Abstractions.Messaging;

namespace Frappe.Identity.Application.Account;

/// <summary>One step: the right password marks the caller's session as freshly stepped up.</summary>
public sealed record CompleteStepUp(Guid PrincipalId, Guid SessionId, string? Password) : ICommand;
