using Frappe.Geo.Domain;
using Frappe.Results;
using Frappe.UseCases;

namespace Frappe.Geo.Application;

/// <summary>
/// Saving a command's changes found that someone else changed the same data since the command read it: nothing of the
/// command was written. Thrown by the storage when the unit of work commits, after the handler returned, so adapters
/// answer it with <see cref="Conflict"/> instead of an unexpected failure.
/// </summary>
public sealed class ConcurrentChangeException : Exception, ICommitRejection
{
    public ConcurrentChangeException()
        : this("Someone else changed the same data first.", null)
    {
    }

    public ConcurrentChangeException(string message)
        : this(message, null)
    {
    }

    public ConcurrentChangeException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <summary>The conflict a client is answered with: it may read again and retry.</summary>
    public static ConflictError Conflict { get; } = new(GeoErrorCodes.HolidayCalendarChangedConcurrently);

    /// <inheritdoc />
    public Error Answer => Conflict;
}
