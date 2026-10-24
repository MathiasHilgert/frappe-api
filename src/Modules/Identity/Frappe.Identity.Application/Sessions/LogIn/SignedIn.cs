namespace Frappe.Identity.Application.Sessions;

/// <summary>A session opened by a successful sign-in; the token is handed out once and only its hash is stored.</summary>
public sealed record SignedIn(Guid PrincipalId, Guid SessionId, string Token, DateTimeOffset ExpiresAt);
