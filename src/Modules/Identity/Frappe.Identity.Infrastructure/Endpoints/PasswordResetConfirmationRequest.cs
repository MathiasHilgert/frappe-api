namespace Frappe.Identity.Infrastructure.Endpoints;

public sealed record PasswordResetConfirmationRequest(string? Code, string? NewPassword);
