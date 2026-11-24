namespace Frappe.Identity.Application.Owners.RegisterOwner;

/// <summary>The answer of a registration: the id to verify with. It is the same shape whether the address was new, taken or replaced.</summary>
/// <param name="Id">The owner's id; for an address a verified owner holds, an id that belongs to nobody.</param>
public sealed record RegisteredOwner(Guid Id);
