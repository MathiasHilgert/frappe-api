namespace Frappe.Identity.Infrastructure.Endpoints;

internal sealed record PasswordResetConfirmationRequest(string? Code, string? NewPassword);
