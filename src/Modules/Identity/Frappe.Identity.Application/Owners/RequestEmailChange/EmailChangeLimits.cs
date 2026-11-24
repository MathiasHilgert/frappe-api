namespace Frappe.Identity.Application.Owners.RequestEmailChange;

/// <summary>What an email change request holds to so that its two branches (a code was sent, none was) cannot be told apart by their time.</summary>
public static class EmailChangeLimits
{
    /// <summary>
    /// The least time a request takes, counted from its start to its answer, commit included: the branch that sends a code writes a code and the
    /// one that does not writes less, so only a floor above both makes them the same. Reuses <see cref="Credentials.ResponseFloor"/>.
    /// </summary>
    public static readonly TimeSpan Floor = TimeSpan.FromMilliseconds(500);
}
