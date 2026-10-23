using Frappe.SharedKernel.Domain;

namespace Frappe.Identity.Domain.Principals;

public sealed record OwnerEmailChangeConfirmed(Guid PrincipalId, string OldEmail, string NewEmail) : IDomainEvent;
