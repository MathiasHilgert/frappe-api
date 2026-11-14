using Frappe.Results;
using Frappe.UseCases;

namespace Frappe.Geo.Application.Territory;

/// <summary>
/// Saving a manual alias change found that another writer changed the same alias first: removed it before this removal
/// or added it (or one equal ignoring case and accents) before this addition, to this subdivision or to another of its
/// country. Nothing of the command was written.
/// Thrown by the storage when the unit of work commits, after the handler returned; <see cref="Answer"/> is what the
/// command would have answered had it run second, so a lost race reads exactly like the sequential case.
/// </summary>
public sealed class SubdivisionAliasRaceLostException : Exception, ICommitRejection
{
    private const string DefaultMessage = "Another writer changed the same subdivision alias first.";

    public SubdivisionAliasRaceLostException()
        : this(DefaultMessage, null)
    {
    }

    public SubdivisionAliasRaceLostException(string message)
        : this(message, null)
    {
    }

    public SubdivisionAliasRaceLostException(string message, Exception? innerException)
        : this(GeoErrors.SubdivisionAliasDuplicate(string.Empty, string.Empty), message, innerException)
    {
    }

    private SubdivisionAliasRaceLostException(Error answer, string message, Exception? innerException)
        : base(message, innerException) =>
        Answer = answer;

    /// <summary>Another writer removed the manual alias first: the removal answers not found.</summary>
    public static SubdivisionAliasRaceLostException Removed(string subdivisionCode, string alias, Exception? innerException) =>
        new(GeoErrors.SubdivisionAliasNotFound(subdivisionCode, alias), DefaultMessage, innerException);

    /// <summary>Another writer added the same alias first: the addition answers duplicate.</summary>
    public static SubdivisionAliasRaceLostException Added(string subdivisionCode, string alias, Exception? innerException) =>
        new(GeoErrors.SubdivisionAliasDuplicate(subdivisionCode, alias), DefaultMessage, innerException);

    /// <summary>Another writer added the same alias to another subdivision of the country first: the addition answers taken.</summary>
    public static SubdivisionAliasRaceLostException Taken(string subdivisionCode, string alias, string otherSubdivisionCode, Exception? innerException) =>
        new(GeoErrors.SubdivisionAliasTaken(subdivisionCode, alias, otherSubdivisionCode), DefaultMessage, innerException);

    /// <summary>The error the client is answered with: not found for a removal, duplicate or taken for an addition.</summary>
    public Error Answer { get; }
}
