namespace Frappe.Identity.Application.Credentials;

/// <summary>How a registration ended, as the registrations counter tags it.</summary>
public enum RegistrationOutcome
{
    /// <summary>A new pending owner.</summary>
    Created,

    /// <summary>A pending owner of the same address was taken over.</summary>
    Replaced,

    /// <summary>A verified owner holds the address: nothing was written and the owner was told.</summary>
    Taken,
}
