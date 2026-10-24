using Frappe.Application.Abstractions.Messaging;

namespace Frappe.Identity.Application.Owners;

/// <summary>Sends a pending owner a new verification code, replacing the previous one. At most five per hour.</summary>
public sealed record ResendVerificationCode(Guid PrincipalId) : ICommand;
