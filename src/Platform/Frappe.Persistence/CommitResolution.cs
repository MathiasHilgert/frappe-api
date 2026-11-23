using Frappe.UseCases;

namespace Frappe.Persistence;

/// <summary>
/// What a rule decides about a <see cref="CommitFailure"/>: <see cref="Reject"/> the command with a rejection, or
/// <see cref="Repair"/> the staged changes so the unit saves again.
/// </summary>
public sealed class CommitResolution
{
    private CommitResolution(Exception? rejection) => Rejection = rejection;

    /// <summary>The rejection to throw; none for a repair.</summary>
    public Exception? Rejection { get; }

    /// <summary>Whether the rule repaired the staged changes, so the unit saves once more.</summary>
    public bool IsRepair => Rejection is null;

    /// <summary>The rule repaired the staged changes (dropped what cannot be written): the unit saves once more.</summary>
    public static CommitResolution Repair { get; } = new(null);

    /// <summary>The command is refused with <paramref name="rejection"/>, answered as <see cref="ICommitRejection.Answer"/>.</summary>
    public static CommitResolution Reject<TRejection>(TRejection rejection)
        where TRejection : Exception, ICommitRejection
    {
        ArgumentNullException.ThrowIfNull(rejection);
        return new CommitResolution(rejection);
    }
}
