using Frappe.Application.Abstractions.Messaging;

namespace Frappe.Identity.Application.Sessions;

/// <summary>Signs the current session out. Other sessions of the same owner stay open.</summary>
public sealed record LogOut(Guid PrincipalId, Guid SessionId) : ICommand;
