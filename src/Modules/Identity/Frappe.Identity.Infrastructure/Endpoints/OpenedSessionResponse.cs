namespace Frappe.Identity.Infrastructure.Endpoints;

/// <summary>The one answer of every endpoint that opens a session: log-in and email verification.</summary>
internal sealed record OpenedSessionResponse(Guid Id, Guid PrincipalId, string Token, DateTimeOffset ExpiresAt);
