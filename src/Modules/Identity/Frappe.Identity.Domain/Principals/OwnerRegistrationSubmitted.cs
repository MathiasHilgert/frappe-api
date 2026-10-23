using Frappe.SharedKernel.Domain;

namespace Frappe.Identity.Domain.Principals;

/// <summary>Events carry plain values only: they leave the aggregate through the outbox.</summary>
public sealed record OwnerRegistrationSubmitted(Guid PrincipalId, string Email) : IDomainEvent;
