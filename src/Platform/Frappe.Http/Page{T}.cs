namespace Frappe.Http;

/// <summary>
/// One page of a list (HTTP-14): its <see cref="Items"/> and the <see cref="NextCursor"/> that asks for the next one,
/// absent on the last page. A response wraps it in the object named by the list's plural (HTTP-15).
/// </summary>
public sealed record Page<T>(IReadOnlyList<T> Items, string? NextCursor)
{
    /// <summary>The same page with each item projected.</summary>
    public Page<TResult> Select<TResult>(Func<T, TResult> projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new Page<TResult>([.. Items.Select(projection)], NextCursor);
    }
}
