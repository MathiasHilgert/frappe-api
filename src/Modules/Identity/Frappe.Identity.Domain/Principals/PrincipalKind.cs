namespace Frappe.Identity.Domain.Principals;

/// <summary>
/// What a principal is. A new kind (staff, terminal, API key) is one more member, its check-constraint migration and its own feature folder.
/// </summary>
public enum PrincipalKind
{
    /// <summary>The owner of one or more businesses, who registers and signs in by email and password.</summary>
    Owner,
}
