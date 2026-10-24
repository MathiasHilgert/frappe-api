namespace Frappe.Identity.Infrastructure.Endpoints;

internal sealed record SessionSummaryResponse(Guid Id, DateTimeOffset CreatedAt, DateTimeOffset LastSeenAt, bool Current);
