using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace Frappe.Platform.Http.Localization;

/// <summary>Resolves the locale a request's error texts are written in.</summary>
internal static class RequestLocale
{
    /// <summary>The query parameter that picks the response locale.</summary>
    public const string QueryParameter = "locale";

    /// <summary>
    /// Returns <c>?locale=</c> when present and valid, else the first valid <c>Accept-Language</c> tag by
    /// quality, else English.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <returns>The locale to write error texts in.</returns>
    public static ErrorLocale Of(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (ErrorLocale.Parse(request.Query[QueryParameter]) is { } query)
        {
            return query;
        }

        IEnumerable<StringWithQualityHeaderValue> languages = request
            .GetTypedHeaders()
            .AcceptLanguage.OrderByDescending(static language => language.Quality ?? 1);
        foreach (StringWithQualityHeaderValue language in languages)
        {
            if (ErrorLocale.Parse(language.Value.Value) is { } tag)
            {
                return tag;
            }
        }

        return ErrorLocale.English;
    }
}
