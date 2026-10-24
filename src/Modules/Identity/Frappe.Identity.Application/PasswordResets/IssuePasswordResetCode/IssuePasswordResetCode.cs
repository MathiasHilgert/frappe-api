using Frappe.Application.Abstractions.Messaging;
namespace Frappe.Identity.Application.PasswordResets;

/// <summary>
/// Issues the reset code <paramref name="ResetId"/> to the owner of <paramref name="Email"/>, if there is one. Runs in
/// the background, after the request was already answered, so whether the account exists never shows in its timing.
/// </summary>
public sealed record IssuePasswordResetCode(Guid ResetId, string Email) : IBackgroundCommand;
