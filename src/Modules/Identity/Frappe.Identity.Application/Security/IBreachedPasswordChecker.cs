namespace Frappe.Identity.Application.Security;

/// <summary>Whether a password is known from public breaches.</summary>
public interface IBreachedPasswordChecker
{
    /// <summary>
    /// True only when the password is known to be breached. False when it is not, and also when the check could
    /// not run: an outage of the breach source must never block a sign-up (fail open).
    /// </summary>
    Task<bool> IsBreachedAsync(string password, CancellationToken cancellationToken);
}
