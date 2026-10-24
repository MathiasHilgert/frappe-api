using Frappe.Application.Abstractions.Messaging;

namespace Frappe.Identity.Application.Account;

/// <summary>
/// Proves the current password, applies the password policy to the new one and lets the aggregate swap the hash,
/// revoking every session except <paramref name="SessionId"/>, the caller's, in the same write.
/// </summary>
public sealed record ChangePassword(Guid PrincipalId, Guid SessionId, string? CurrentPassword, string? NewPassword) : ICommand;
