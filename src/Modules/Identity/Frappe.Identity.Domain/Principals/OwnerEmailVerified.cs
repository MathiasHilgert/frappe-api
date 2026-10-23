using Frappe.SharedKernel.Domain;

namespace Frappe.Identity.Domain.Principals;

public sealed record OwnerEmailVerified(Guid PrincipalId) : IDomainEvent;
