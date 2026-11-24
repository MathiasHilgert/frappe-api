namespace Frappe.Identity.Application.Owners.RequestPasswordReset;

/// <summary>What the reset request holds to so that its two branches (a code was sent, none was) cannot be told apart by their time (S11).</summary>
public static class PasswordResetLimits
{
    /// <summary>
    /// The least time a reset request takes, counted from the start of the request to its answer, commit included: the branch that sends a code
    /// writes and the one that does not writes nothing, so only a floor above both makes them the same.
    /// Reuses <see cref="Credentials.ResponseFloor"/>.
    /// </summary>
    public static readonly TimeSpan Floor = TimeSpan.FromMilliseconds(500);
}
