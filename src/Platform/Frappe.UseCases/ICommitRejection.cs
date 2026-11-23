using Frappe.Results;

namespace Frappe.UseCases;

/// <summary>
/// An exception the unit of work throws at commit when the storage refuses the use case's changes for an expected
/// reason, such as someone else changing the same data first. Nothing was written, and the caller is answered
/// <see cref="Answer"/>: it is a rejection like a failed <see cref="Result{T}"/>, not an incident.
/// </summary>
public interface ICommitRejection
{
    /// <summary>The error the caller is answered with.</summary>
    Error Answer { get; }
}
