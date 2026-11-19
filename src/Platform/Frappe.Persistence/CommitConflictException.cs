using Frappe.Results;
using Frappe.UseCases;

namespace Frappe.Persistence;

/// <summary>
/// The commit kept failing after its rules repaired the staged changes the maximum number of times: other writers keep
/// moving the same rows. Nothing was written; the caller may read again and retry.
/// </summary>
public sealed class CommitConflictException : Exception, ICommitRejection
{
    public CommitConflictException()
        : this("The commit kept conflicting with other writers.", null)
    {
    }

    public CommitConflictException(string message)
        : this(message, null)
    {
    }

    public CommitConflictException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <inheritdoc />
    public Error Answer { get; } = new ConflictError(ICommitRejection.CommitConflictCode);
}
