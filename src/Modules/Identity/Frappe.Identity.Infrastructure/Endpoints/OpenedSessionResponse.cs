namespace Frappe.Identity.Infrastructure.Endpoints;

internal sealed record OpenedSessionResponse(Guid Id, Guid PrincipalId, string Token, DateTimeOffset ExpiresAt);
