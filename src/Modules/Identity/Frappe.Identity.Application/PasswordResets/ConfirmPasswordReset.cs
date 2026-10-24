using Frappe.Application.Abstractions.Messaging;
namespace Frappe.Identity.Application.PasswordResets;

public sealed record ConfirmPasswordReset(Guid ResetId, string? Code, string? NewPassword, string ClientIp) : ICommand;
