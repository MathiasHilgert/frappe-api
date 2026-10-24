namespace Frappe.Identity.Infrastructure.Endpoints;

public sealed record SessionSummaryResponse(Guid Id, DateTimeOffset CreatedAt, DateTimeOffset LastSeenAt, bool Current);
