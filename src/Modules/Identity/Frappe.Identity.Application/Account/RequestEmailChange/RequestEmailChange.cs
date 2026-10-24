using Frappe.Application.Abstractions.Messaging;
namespace Frappe.Identity.Application.Account;

/// <summary>Asks to move the caller's sign-in to <paramref name="NewEmail"/>; the caller already passed the step-up.</summary>
public sealed record RequestEmailChange(Guid PrincipalId, string? NewEmail) : ICommand;
