namespace Frappe.Identity.Infrastructure.Endpoints;

public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);
