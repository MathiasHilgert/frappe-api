namespace Frappe.Identity.Application.Credentials;

/// <summary>How a log-in attempt ended, as <c>frappe.identity.logins</c> counts it.</summary>
public enum LoginOutcome
{
    /// <summary>The password was right and a session opened.</summary>
    Opened,

    /// <summary>The password was right but the address is not verified: a new code was sent.</summary>
    Pending,

    /// <summary>The credentials were wrong, for whatever reason.</summary>
    Invalid,

    /// <summary>The address made more attempts than its window allows.</summary>
    Throttled,
}
