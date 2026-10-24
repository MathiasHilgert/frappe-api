using Frappe.Application.Abstractions.Messaging;

namespace Frappe.Identity.Application.Sessions;

/// <summary>Ends one of the caller's own sessions; another principal's session is simply not found.</summary>
public sealed record RevokeSession(Guid PrincipalId, Guid SessionId) : ICommand;
