using Frappe.SharedKernel.Domain;

namespace Frappe.Identity.Domain.Principals;

public sealed record OwnerSuspended(Guid PrincipalId) : IDomainEvent;
