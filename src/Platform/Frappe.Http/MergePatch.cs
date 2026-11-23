using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace Frappe.Http;

/// <summary>
/// JSON Merge Patch (RFC 7396), the format of every Frappé <c>PATCH</c>, and the header that advertises it (RFC 5789),
/// written once for the platform's <c>415</c> and every endpoint that patches.
/// </summary>
public static class MergePatch
{
    /// <summary>The media type of a merge patch body.</summary>
    public const string MediaType = "application/merge-patch+json";

    /// <summary>The response header naming the patch formats a resource accepts (RFC 5789 §3.1).</summary>
    public const string AcceptPatchHeader = "Accept-Patch";

    /// <summary>Whether the body of <paramref name="request"/> is a merge patch, whatever the case and parameters (<c>charset</c>) of its
    /// type.</summary>
    public static bool IsBodyOf(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return MediaTypeHeaderValue.TryParse(request.ContentType, out var type) && type.MediaType.Equals(MediaType,
            StringComparison.OrdinalIgnoreCase);
    }
}
