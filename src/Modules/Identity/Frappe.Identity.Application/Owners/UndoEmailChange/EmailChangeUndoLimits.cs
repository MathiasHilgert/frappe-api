namespace Frappe.Identity.Application.Owners.UndoEmailChange;

/// <summary>What an undo holds to so that its answers (a token that works, one that does not) cannot be told apart by their time.</summary>
public static class EmailChangeUndoLimits
{
    /// <summary>
    /// The least time an undo takes, counted from its start to its answer, commit included: a token that restores the address writes much more
    /// than one that is refused, so only a floor above both makes them the same. Reuses <see cref="Credentials.ResponseFloor"/>.
    /// </summary>
    public static readonly TimeSpan Floor = TimeSpan.FromMilliseconds(500);
}
