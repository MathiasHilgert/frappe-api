namespace Frappe.Identity.Domain.Sessions;

/// <summary>Which limit ended a session that nobody revoked.</summary>
public enum SessionEndReason
{
    /// <summary>It was not used for as long as the idle timeout.</summary>
    Idle,

    /// <summary>It reached its absolute lifetime.</summary>
    Absolute,
}
