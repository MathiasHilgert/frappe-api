namespace Frappe.Platform.Http.Paging;

/// <summary>The page size of a cursor-paged list.</summary>
public static class PageLimit
{
    /// <summary>The page size when the client sends no <c>limit</c>.</summary>
    public const int Default = 50;

    /// <summary>The largest page size; a larger <c>limit</c> is clamped to it.</summary>
    public const int Max = 200;

    /// <summary>Clamps the client's <c>limit</c> to 1..<see cref="Max"/>, or <see cref="Default"/> when absent.</summary>
    /// <param name="limit">The <c>limit</c> query parameter.</param>
    /// <returns>The page size to read.</returns>
    public static int Of(int? limit) => limit is { } value ? Math.Clamp(value, 1, Max) : Default;
}
