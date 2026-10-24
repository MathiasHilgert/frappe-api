namespace Frappe.Identity.Infrastructure.Endpoints;

public sealed record LogInRequest(string? Email, string? Password);
