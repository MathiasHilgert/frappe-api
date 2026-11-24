namespace Frappe.Identity.Application.Credentials;

/// <summary>What the breach list said about a password.</summary>
public enum BreachCheckOutcome
{
    /// <summary>The list does not hold the password.</summary>
    Clean,

    /// <summary>The list holds the password.</summary>
    Breached,

    /// <summary>The list gave no answer, so the password was accepted.</summary>
    Unavailable,
}
