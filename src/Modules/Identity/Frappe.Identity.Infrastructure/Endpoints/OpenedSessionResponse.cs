namespace Frappe.Identity.Infrastructure.Endpoints;

public sealed record OpenedSessionResponse(Guid Id, Guid PrincipalId, string Token, DateTimeOffset ExpiresAt);
