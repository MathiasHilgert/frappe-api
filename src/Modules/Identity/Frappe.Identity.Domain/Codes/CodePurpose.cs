namespace Frappe.Identity.Domain.Codes;

/// <summary>What a one-time code proves; one mechanism serves every purpose.</summary>
public enum CodePurpose
{
    /// <summary>The address belongs to whoever registered.</summary>
    EmailVerification,

    /// <summary>Whoever asked to reset the password controls the address.</summary>
    PasswordReset,

    /// <summary>Whoever asked to change the address controls the new one.</summary>
    EmailChange,
}
