using Microsoft.AspNetCore.Http;

namespace Frappe.Geo.Infrastructure.Catalogues;

/// <summary>
/// What narrows the caching of one response below the dataset's: a suffix that tells its tag apart and a shorter
/// max-age. An endpoint stores it in the request, and the caching filter applies it, on a 304 too.
/// </summary>
/// <param name="TagSuffix">Appended to the dataset tag, such as the default year a response resolved.</param>
/// <param name="MaxAge">The most seconds the response may be cached.</param>
internal sealed record CacheScope(string TagSuffix, int MaxAge)
{
    private static readonly object _key = new();

    public static void Set(HttpContext context, CacheScope scope) => context.Items[_key] = scope;

    public static CacheScope? Of(HttpContext context) =>
        context.Items.TryGetValue(_key, out object? scope) ? scope as CacheScope : null;
}
