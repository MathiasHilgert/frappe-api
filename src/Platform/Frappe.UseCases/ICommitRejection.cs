using Frappe.Results;

namespace Frappe.UseCases;

/// <summary>
/// Implemented by the exception the unit of work throws at commit when the storage refuses the use case's changes for
/// an expected reason, such as someone else changing the same data first. It is the port the HTTP layer recognises.
/// Nothing was written, and the caller is answered <see cref="Answer"/>: it is a rejection like a failed
/// <see cref="Result{T}"/>, not an incident.
/// </summary>
public interface ICommitRejection
{
    /// <summary>
    /// The code of the conflict a client is answered with when a commit kept failing against other writers. Defined here,
    /// not in the storage adapter that throws it, so the HTTP layer can recognise it without depending on storage.
    /// </summary>
    public const string CommitConflictCode = "platform.persistence.commit_conflict";

    /// <summary>The error the caller is answered with.</summary>
    Error Answer { get; }
}
