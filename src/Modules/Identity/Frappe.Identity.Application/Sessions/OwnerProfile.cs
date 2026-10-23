namespace Frappe.Identity.Application.Sessions;

/// <summary>The signed-in owner's own profile.</summary>
public sealed record OwnerProfile(Guid Id, string Name, string Email, string Status, string Locale);
