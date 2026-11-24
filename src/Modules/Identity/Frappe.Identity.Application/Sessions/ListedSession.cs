using System.Net;
using NodaTime;

namespace Frappe.Identity.Application.Sessions;

/// <summary>A live session as the list reads it, before its user agent is parsed.</summary>
/// <param name="Id">The session.</param>
/// <param name="OpenedAt">When it opened.</param>
/// <param name="LastSeenAt">When it was last used (kept up to a minute late).</param>
/// <param name="UserAgent">The user agent as sent, if any.</param>
/// <param name="IpPrefix">The client address truncated to its IPv4 /24 or IPv6 /48, if known.</param>
public sealed record ListedSession(Guid Id, Instant OpenedAt, Instant LastSeenAt, string? UserAgent, IPNetwork? IpPrefix);
