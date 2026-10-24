namespace Frappe.Identity.Infrastructure.Endpoints;

public sealed record VerifiedOwnerResponse(Guid SessionId, string Token, DateTimeOffset ExpiresAt);
