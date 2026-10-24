namespace Frappe.Identity.Infrastructure.Endpoints;

public sealed record RegisterOwnerRequest(string? Name, string? Email, string? Password);
