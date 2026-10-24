namespace Frappe.Identity.Contracts.Queries;

/// <summary>An owner's name (for a greeting), current email address and stored BCP-47 locale.</summary>
/// <param name="Name">The owner's display name.</param>
/// <param name="Email">The address mail goes to.</param>
/// <param name="Locale">The language the owner reads.</param>
public sealed record OwnerContact(string Name, string Email, string Locale);
