namespace Frappe.Identity.Domain.Sessions;

/// <summary>Why a session was revoked.</summary>
public enum RevocationReason
{
    /// <summary>The holder logged out.</summary>
    Logout,

    /// <summary>The holder revoked this session from another one.</summary>
    RevokedByHolder,

    /// <summary>The holder revoked every session but the current one.</summary>
    OthersRevoked,

    /// <summary>The password was changed.</summary>
    PasswordChanged,

    /// <summary>The password was reset.</summary>
    PasswordReset,

    /// <summary>The email address changed.</summary>
    EmailChanged,

    /// <summary>The email change was undone from the notice sent to the old address.</summary>
    EmailChangeUndone,
}
