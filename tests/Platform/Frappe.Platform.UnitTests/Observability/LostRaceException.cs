using Frappe.Results;
using Frappe.UseCases;

namespace Frappe.Platform.UnitTests.Observability;

/// <summary>What a storage throws when someone else changed the same data first: an expected answer, not an incident.</summary>
public sealed class LostRaceException : Exception, ICommitRejection
{
    public LostRaceException()
        : base("Someone else changed it first.")
    {
    }

    public LostRaceException(string message)
        : base(message)
    {
    }

    public LostRaceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public Error Answer { get; } = new ConflictError("test.changed_concurrently");
}
