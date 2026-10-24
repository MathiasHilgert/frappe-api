namespace Frappe.Identity.Infrastructure.Endpoints;

internal sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);
