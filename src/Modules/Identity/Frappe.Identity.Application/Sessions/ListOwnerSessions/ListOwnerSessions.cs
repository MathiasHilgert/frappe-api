using Frappe.Application.Abstractions.Messaging;

namespace Frappe.Identity.Application.Sessions;

/// <summary>The caller's own live sessions; <paramref name="CurrentSessionId"/> is the one asking.</summary>
public sealed record ListOwnerSessions(Guid PrincipalId, Guid CurrentSessionId) : IQuery<IReadOnlyList<SessionSummary>>;
