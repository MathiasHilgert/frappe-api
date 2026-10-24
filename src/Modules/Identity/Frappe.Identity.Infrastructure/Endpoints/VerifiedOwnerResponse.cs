namespace Frappe.Identity.Infrastructure.Endpoints;

internal sealed record VerifiedOwnerResponse(Guid SessionId, string Token, DateTimeOffset ExpiresAt);
