using Frappe.Localization;
using Microsoft.AspNetCore.Http;

namespace Frappe.Http;

/// <summary>The language an endpoint answers in, and the <c>Content-Language</c> it declares for what it served.</summary>
public static class ServedLanguages
{
    /// <summary>
    /// The negotiated request language (<see cref="LanguageNegotiation.NegotiateRequestLanguage"/>). Throws when the
    /// endpoint is mapped outside a group that negotiates it, which is a wiring mistake, never a client error.
    /// </summary>
    public static LanguageTag LanguageOf(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        return (httpContext.Features.Get<RequestLanguage>()
            ?? throw new InvalidOperationException("The endpoint must be mapped in a group that negotiates the request language.")).Tag;
    }

    /// <summary>
    /// Declares <c>Content-Language</c> as every distinct language served (ordinal order). Texts with no language (proper
    /// nouns, identifiers) are <see langword="null"/> and declare nothing; with none at all the header is not set.
    /// </summary>
    public static void Declare(HttpContext httpContext, IEnumerable<LanguageTag?> served)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(served);

        var languages = served.OfType<LanguageTag>().Select(l => l.Value).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (languages.Length > 0)
        {
            httpContext.Response.Headers.ContentLanguage = string.Join(", ", languages);
        }
    }
}
