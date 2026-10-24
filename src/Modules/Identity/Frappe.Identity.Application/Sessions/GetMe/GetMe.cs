using Frappe.Application.Abstractions.Messaging;

namespace Frappe.Identity.Application.Sessions;

/// <summary>The signed-in owner's own profile; null when the principal is gone.</summary>
public sealed record GetMe(Guid PrincipalId) : IQuery<OwnerProfile?>;
