using Frappe.Aggregates;

namespace Frappe.Notifications.Domain.Suppressions;

/// <summary>An address stopped receiving notifications over a channel. Counted once the commit lands (S15); it carries no address (S13).</summary>
/// <param name="Reason">Why it was suppressed.</param>
public sealed record SuppressionAdded(SuppressionReason Reason) : IDomainEvent;
