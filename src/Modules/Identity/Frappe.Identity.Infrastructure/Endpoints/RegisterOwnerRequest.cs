namespace Frappe.Identity.Infrastructure.Endpoints;

internal sealed record RegisterOwnerRequest(string? Name, string? Email, string? Password);
