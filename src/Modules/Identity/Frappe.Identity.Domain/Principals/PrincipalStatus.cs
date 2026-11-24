namespace Frappe.Identity.Domain.Principals;

/// <summary>Where a principal stands. Authentication rejects <see cref="Pending"/>.</summary>
public enum PrincipalStatus
{
    /// <summary>Registered but not yet proven (an unverified address, or a staff member or terminal waiting to be paired).</summary>
    Pending,

    /// <summary>Allowed to authenticate.</summary>
    Active,
}
