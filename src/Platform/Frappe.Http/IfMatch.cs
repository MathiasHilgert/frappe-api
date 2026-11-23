using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace Frappe.Http;

/// <summary>
/// The <c>If-Match</c> precondition (RFC 9110 §13.1.1) as every Frappé <c>PATCH</c> reads it: compared strongly, so a listed strong tag
/// equal to the current one, or <c>*</c>, matches and a weak tag never does. It is optional (never 428): a request without it changes
/// the resource unconditionally.
/// </summary>
public static class IfMatch
{
    /// <summary>The tag that stands for <c>*</c>: any current version matches.</summary>
    public const string Any = "*";

    /// <summary>
    /// The tags <c>If-Match</c> accepts, unquoted, or <see langword="null"/> when the request sends none. <c>*</c> is <see cref="Any"/>;
    /// weak tags are dropped, as strong comparison never matches them; a header that cannot be read as a list of entity tags accepts
    /// nothing, so the change is refused rather than applied unconditionally.
    /// </summary>
    public static IReadOnlyList<string>? Read(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var values = request.Headers.IfMatch;
        if (StringValues.IsNullOrEmpty(values))
        {
            return null;
        }

        if (!EntityTagHeaderValue.TryParseStrictList(values, out var tags))
        {
            return [];
        }

        return [.. tags.Where(tag => tag.Equals(EntityTagHeaderValue.Any) || !tag.IsWeak)
            .Select(tag => tag.Equals(EntityTagHeaderValue.Any) ? Any : tag.Tag.Value!.Trim('"'))];
    }
}
