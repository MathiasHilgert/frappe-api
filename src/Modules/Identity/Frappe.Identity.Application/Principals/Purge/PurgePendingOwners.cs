namespace Frappe.Identity.Application.Principals.Purge;

/// <summary>Deletes unverified registrations past their grace. Published daily by a recurring job.</summary>
public sealed record PurgePendingOwners;
