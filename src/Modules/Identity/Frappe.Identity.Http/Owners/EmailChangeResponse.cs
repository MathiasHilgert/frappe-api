namespace Frappe.Identity.Http.Owners;

/// <summary>The answer of an email change request; it has the same shape whether a code was sent or not.</summary>
/// <param name="Id">The id to confirm the change with.</param>
public sealed record EmailChangeResponse(Guid Id);
