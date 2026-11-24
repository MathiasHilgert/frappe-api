using Frappe.Results;
using Frappe.UseCases;

namespace Frappe.Identity.Infrastructure.Owners;

/// <summary>
/// A commit the storage refused because another request won a race on the same code, token or address (UOW-05). Nothing was written, and the
/// loser is answered what a repeat gets: the same rule violation (<see cref="Answer"/>), never a conflict that tells it somebody else was there.
/// </summary>
public sealed class OwnerCommitRejectionException(Error answer, Exception inner)
    : Exception("Another request won the race.", inner), ICommitRejection
{
    /// <inheritdoc />
    public Error Answer { get; } = answer;
}
