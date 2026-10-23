using Frappe.SharedKernel.Domain;

namespace Frappe.Identity.Domain.Principals;

public sealed record OwnerPasswordChanged(Guid PrincipalId) : IDomainEvent;
