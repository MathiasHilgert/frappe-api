using Frappe.SharedKernel.Domain;

namespace Frappe.Identity.Domain.Principals;

public sealed record OwnerEmailChangeRequested(Guid PrincipalId, string NewEmail) : IDomainEvent;
