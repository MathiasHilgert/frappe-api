namespace Frappe.Identity.Application.Sessions;

/// <summary>One of the caller's live sessions, as shown to them. Never carries the token or its hash.</summary>
public sealed record SessionSummary(Guid Id, DateTimeOffset CreatedAt, DateTimeOffset LastSeenAt, bool Current);
