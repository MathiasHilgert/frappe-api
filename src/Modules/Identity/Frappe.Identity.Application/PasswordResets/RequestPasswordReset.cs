namespace Frappe.Identity.Application.PasswordResets;

public sealed record RequestPasswordReset(string? Email, string ClientIp);
