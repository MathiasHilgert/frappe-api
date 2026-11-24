namespace Frappe.Identity.Application.Owners.RequestEmailChange;

/// <summary>What an email change request answers: the id the confirmation names, the code's when one was sent and a made-up one when not.</summary>
/// <param name="Id">A UUIDv7 that the confirmation is addressed with.</param>
public sealed record EmailChangeRequested(Guid Id);
