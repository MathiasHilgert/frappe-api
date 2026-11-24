using Frappe.Persistence;

namespace Frappe.Identity.Infrastructure.Sessions;

/// <summary>
/// What a refused commit means for a session (UOW-05): another request changed the same session first, most often two requests of one session
/// touching it in the same minute. Nothing is written and the command is answered a commit conflict; a touch is never retried (the winner did
/// the same work), and a client may retry anything else.
/// </summary>
internal static class SessionRejections
{
    /// <summary>The SQLSTATE of a deadlock Postgres broke by cancelling this transaction.</summary>
    private const string DeadlockDetected = "40P01";

    public static void Register(CommitRejectionMap rejections)
    {
        ArgumentNullException.ThrowIfNull(rejections);
        rejections
            .OnPostgres(failure => failure.Postgres?.SqlState == DeadlockDetected ? Conflict(failure) : null)
            .OnConcurrency(Conflict);
    }

    private static CommitResolution Conflict(CommitFailure failure) =>
        CommitResolution.Reject(new CommitConflictException("Another request changed the same session first.", failure.Exception));
}
