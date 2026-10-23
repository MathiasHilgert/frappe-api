using Frappe.SharedKernel.Domain;

namespace Frappe.Identity.Domain.Principals;

public sealed record SessionOpened(Guid PrincipalId, Guid SessionId, SessionSurface Surface) : IDomainEvent;
