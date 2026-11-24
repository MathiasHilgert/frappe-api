namespace Frappe.Identity.Http.Sessions;

/// <summary>One live session of the caller.</summary>
/// <param name="Id">The session; the id <c>DELETE /me/sessions/{sessionId}</c> takes.</param>
/// <param name="Current">Whether it is the session this request is using.</param>
/// <param name="Browser">The browser its user agent names (such as Chrome); null when not recognized.</param>
/// <param name="Os">The operating system its user agent names (such as Windows); null when not recognized.</param>
/// <param name="IpPrefix">The client address as an IPv4 /24 or IPv6 /48 network, never the whole address; null when unknown.</param>
/// <param name="OpenedAt">When the session opened.</param>
/// <param name="LastSeenAt">When the session was last used; it can be up to a minute behind.</param>
public sealed record MySessionResponse(
    Guid Id, bool Current, string? Browser, string? Os, string? IpPrefix, DateTimeOffset OpenedAt, DateTimeOffset LastSeenAt);
