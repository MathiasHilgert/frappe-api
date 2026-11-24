namespace Frappe.Identity.Http.Owners;

/// <summary>The answer of a registration; it has the same shape whether the address was new, taken or replaced.</summary>
/// <param name="Id">The id to verify the address with.</param>
public sealed record RegisteredOwnerResponse(Guid Id);
