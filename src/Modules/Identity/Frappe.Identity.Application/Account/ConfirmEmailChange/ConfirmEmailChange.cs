using Frappe.Application.Abstractions.Messaging;

namespace Frappe.Identity.Application.Account;

/// <summary>Swaps the owner's email when the code sent to the new address comes back.</summary>
public sealed record ConfirmEmailChange(Guid PrincipalId, Guid ChangeId, string? Code) : ICommand;
