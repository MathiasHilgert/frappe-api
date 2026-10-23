using Frappe.SharedKernel.Domain;

namespace Frappe.Identity.Domain.Principals;

public sealed record SessionRevoked(Guid PrincipalId, Guid SessionId) : IDomainEvent;
