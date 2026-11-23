using System.Globalization;
using Frappe.Results;

namespace Frappe.Http;

/// <summary>Binds a client's <see cref="PageRequest{TPosition}"/>.</summary>
public static class PageRequest
{
    /// <summary>
    /// Binds <paramref name="limit"/> and <paramref name="cursor"/>. A cursor is valid only for the
    /// <paramref name="scope"/> it was issued for (what fixes the list: the parent resource and the search), so a client
    /// cannot carry a position into another listing; one this platform did not issue is invalid. Both problems are told
    /// together. The scope fingerprint is not a secret, so anyone can forge a cursor for a scope: a list whose position
    /// the database cannot take as any value (a date out of range) passes <paramref name="isValid"/>, and a position
    /// it refuses makes the cursor invalid, never a failure further down.
    /// </summary>
    public static Result<PageRequest<TPosition>> Bind<TPosition>(string? limit, string? cursor, string scope, Func<TPosition, bool>? isValid = null)
        where TPosition : class
    {
        ArgumentNullException.ThrowIfNull(scope);

        var errors = new List<Error>();
        var size = ParseLimit(limit, errors);
        var after = cursor is null ? null : PageCursor.Decode<TPosition>(cursor, scope);
        if (after is not null && isValid is not null && !isValid(after))
        {
            after = null;
        }

        if (cursor is not null && after is null)
        {
            errors.Add(Error.InvalidValue(PageLimits.CursorInvalidCode));
        }

        return errors.Count > 0 ? Result.Failure<PageRequest<TPosition>>(errors) : new PageRequest<TPosition>(size, after);
    }

    private static int ParseLimit(string? raw, List<Error> errors)
    {
        if (raw is null)
        {
            return PageLimits.Default;
        }

        if (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var limit))
        {
            errors.Add(Error.InvalidValue(PageLimits.LimitInvalidCode, ("value", raw), ("min", PageLimits.Min), ("max", PageLimits.Max)));
            return PageLimits.Default;
        }

        if (limit is < PageLimits.Min or > PageLimits.Max)
        {
            errors.Add(Error.InvalidValue(PageLimits.LimitOutOfRangeCode, ("value", raw), ("min", PageLimits.Min), ("max", PageLimits.Max)));
            return PageLimits.Default;
        }

        return limit;
    }
}
