namespace Frappe.Identity.Infrastructure.Endpoints;

internal sealed record LogInRequest(string? Email, string? Password);
