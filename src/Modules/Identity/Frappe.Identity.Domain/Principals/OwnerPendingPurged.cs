using Frappe.SharedKernel.Domain;

namespace Frappe.Identity.Domain.Principals;

/// <summary>An unverified registration outlived its grace and was deleted. Carries the id only: the address is gone.</summary>
public sealed record OwnerPendingPurged(Guid PrincipalId) : IDomainEvent;
