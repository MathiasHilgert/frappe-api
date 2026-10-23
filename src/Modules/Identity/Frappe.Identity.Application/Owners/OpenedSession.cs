namespace Frappe.Identity.Application.Owners;

/// <summary>The session handed out on sign-in. <see cref="Token"/> is shown once; only its hash is stored.</summary>
public sealed record OpenedSession(Guid SessionId, string Token, DateTimeOffset ExpiresAt);
