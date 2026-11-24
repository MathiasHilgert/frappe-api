using NodaTime;

namespace Frappe.Identity.Application.Sessions;

/// <summary>A session just opened, staged and not yet saved: what the log-in answer carries.</summary>
/// <param name="Id">The session.</param>
/// <param name="Token">The token to hand to the client; it exists nowhere else.</param>
/// <param name="IdleExpiresAt">The session idles out when unused by then.</param>
/// <param name="AbsoluteExpiresAt">The session ends by then, however much it is used.</param>
public sealed record OpenedSession(Guid Id, IssuedToken Token, Instant IdleExpiresAt, Instant AbsoluteExpiresAt);
