using Frappe.Localization;
using Microsoft.AspNetCore.Http;

namespace Frappe.Http;

/// <summary>The language an endpoint answers in, and the <c>Content-Language</c> it declares for what it served.</summary>
public static class ServedLanguages
{
    private const string ServedKey = "frappe.served-languages";

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
    /// Declares <c>Content-Language</c> as every distinct language served so far in the request (ordinal order), the
    /// ones given here together with those other texts of the same response declared (<see cref="Declare"/> may be
    /// called by each piece that writes text). Texts with no language (proper nouns, identifiers) are
    /// <see langword="null"/> and declare nothing; with none at all the header is not set.
    /// </summary>
    public static void Declare(HttpContext httpContext, IEnumerable<LanguageTag?> served)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(served);

        if (!httpContext.Items.TryGetValue(ServedKey, out var item) || item is not HashSet<string> languages)
        {
            languages = [with(StringComparer.Ordinal)];
            httpContext.Items[ServedKey] = languages;
        }

        languages.UnionWith(served.OfType<LanguageTag>().Select(l => l.Value));
        if (languages.Count > 0)
        {
            httpContext.Response.Headers.ContentLanguage = string.Join(", ", languages.Order(StringComparer.Ordinal));
        }
    }
}
