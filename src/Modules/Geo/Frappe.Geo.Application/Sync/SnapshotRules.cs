using System.Globalization;
using Frappe.Results;

namespace Frappe.Geo.Application.Sync;

/// <summary>
/// The validations every source composes instead of writing its own. Each returns the errors it found (none when the
/// rule holds), so a source reports every problem of a download at once.
/// </summary>
public static class SnapshotRules
{
    public const string EmptyCode = "geo.sync.snapshot.empty";
    public const string DuplicateCode = "geo.sync.snapshot.duplicate";
    public const string InvalidRowCode = "geo.sync.snapshot.invalid_row";
    public const string UnreadableCode = "geo.sync.snapshot.unreadable";

    public static IEnumerable<Error> NotEmpty<T>(string what, IReadOnlyCollection<T> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return rows.Count == 0 ? [Error.RuleViolation(EmptyCode, ("what", what))] : [];
    }

    public static IEnumerable<Error> UniqueBy<T>(string what, IEnumerable<T> rows, Func<T, string> keyOf) =>
        rows.GroupBy(keyOf, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => Error.RuleViolation(DuplicateCode, ("what", what), ("key", group.Key)));

    /// <summary>Every row that breaks <paramref name="isValid"/>, named by its key.</summary>
    public static IEnumerable<Error> Each<T>(string what, IEnumerable<T> rows, Func<T, string> keyOf, Func<T, bool> isValid) =>
        rows.Where(row => !isValid(row)).Select(row => Error.RuleViolation(InvalidRowCode, ("what", what), ("key", keyOf(row))));

    public static Error Unreadable(string resource, string reason) =>
        Error.RuleViolation(UnreadableCode, ("resource", resource), ("reason", reason));

    /// <summary>The errors as the result of reading a snapshot: the snapshot when there are none.</summary>
    public static Result<T> Conclude<T>(T snapshot, IEnumerable<Error> errors)
    {
        var found = errors.ToArray();
        return found.Length == 0 ? snapshot : Result.Failure<T>(found);
    }

    /// <summary>A short, log-safe text of a refused snapshot's errors (at most <paramref name="max"/>).</summary>
    public static string Describe(IReadOnlyList<Error> errors, int max = 10)
    {
        ArgumentNullException.ThrowIfNull(errors);
        var shown = errors.Take(max).Select(e => e.Code + "(" + string.Join(", ", e.Parameters.Select(p => p.Key + "=" + Convert.ToString(p.Value, CultureInfo.InvariantCulture))) + ")");
        return string.Join("; ", shown) + (errors.Count > max ? $"; and {errors.Count - max} more" : string.Empty);
    }
}
