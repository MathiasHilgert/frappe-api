using System.Net;
using NodaTime;

namespace Frappe.Identity.Application.Sessions.ListMySessions;

/// <summary>A live session of the caller, as the session list shows it.</summary>
/// <param name="Id">The session.</param>
/// <param name="Current">Whether it is the session the request is using.</param>
/// <param name="Browser">The browser its user agent names; none when not recognized.</param>
/// <param name="Os">The operating system its user agent names; none when not recognized.</param>
/// <param name="IpPrefix">The client address truncated to its IPv4 /24 or IPv6 /48; none when unknown.</param>
/// <param name="OpenedAt">When it opened.</param>
/// <param name="LastSeenAt">When it was last used, up to a minute late.</param>
public sealed record MySessionView(Guid Id, bool Current, string? Browser, string? Os, IPNetwork? IpPrefix, Instant OpenedAt, Instant LastSeenAt);
