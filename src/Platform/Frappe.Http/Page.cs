namespace Frappe.Http;

/// <summary>Builds the <see cref="Page{T}"/> of a list.</summary>
public static class Page
{
    /// <summary>
    /// The page of <paramref name="items"/>; <paramref name="next"/> is the position it ended at, or
    /// <see langword="null"/> on the last page. The cursor is bound to <paramref name="scope"/> (see
    /// <see cref="PageRequest.Bind{TPosition}"/>).
    /// </summary>
    public static Page<T> Of<T, TPosition>(IReadOnlyList<T> items, TPosition? next, string scope)
        where TPosition : class
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(scope);
        return new Page<T>(items, next is null ? null : PageCursor.Encode(next, scope));
    }
}
