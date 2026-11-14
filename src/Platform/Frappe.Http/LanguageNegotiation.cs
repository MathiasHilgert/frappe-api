using Frappe.Localization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace Frappe.Http;

/// <summary>
/// Marks an endpoint whose response depends on the language the client asks for; documentation reads it to declare
/// the <c>Accept-Language</c> header.
/// </summary>
#pragma warning disable S2094 // Endpoint metadata is looked up by type; the marker carries no data on purpose.
public sealed class NegotiatesRequestLanguageMetadata;
#pragma warning restore S2094

/// <summary>
/// Language negotiation. The first language the client accepts that Frappé is written in becomes the
/// <see cref="RequestLanguage"/>; without one the request is served in English. Each response says in
/// <c>Content-Language</c> which language it is actually written in, so a fallback is never hidden.
/// </summary>
public static class LanguageNegotiation
{
    /// <summary>
    /// Negotiates the language on every endpoint of the group (see <see cref="Negotiate"/>). Responses vary by
    /// <c>Accept-Language</c>, so shared caches keep one copy per language.
    /// </summary>
    public static TBuilder NegotiateRequestLanguage<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.WithMetadata(new NegotiatesRequestLanguageMetadata());
        return builder.AddEndpointFilter(async (context, next) =>
        {
            Apply(context.HttpContext);
            return await next(context);
        });
    }

    /// <summary>
    /// The language to answer in, following RFC 4647 Lookup (RFC 9110 §12.5.4): the ranges are tried from the highest
    /// quality down (in header order on ties), and the first one Frappé is written in, itself or narrowed
    /// (<see cref="LanguageFallback.IsServed"/>), wins: <c>fr, es-AR;q=0.8</c> is <c>es-AR</c>. English is the answer
    /// only when no range is served, or the header is missing, a wildcard or malformed.
    /// </summary>
    public static LanguageTag Negotiate(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (StringWithQualityHeaderValue.TryParseStrictList(request.Headers.AcceptLanguage, out var ranges))
        {
            foreach (var range in ranges.Where(r => (r.Quality ?? 1) > 0).OrderByDescending(r => r.Quality ?? 1))
            {
                if (LanguageTag.Create(range.Value.Value) is { IsSuccess: true } tag && LanguageFallback.IsServed(tag.Value))
                {
                    return tag.Value;
                }
            }
        }

        return LanguageFallback.English;
    }

    /// <summary>
    /// Negotiates the request language once (a request that already has one keeps it) and declares that the response
    /// varies by <c>Accept-Language</c>. Also used by the platform's own problems, which answer before or without an
    /// endpoint (a rate-limited request, an unknown route).
    /// </summary>
    internal static RequestLanguage Apply(HttpContext httpContext)
    {
        var language = httpContext.Features.Get<RequestLanguage>();
        if (language is null)
        {
            language = new RequestLanguage(Negotiate(httpContext.Request));
            httpContext.Features.Set(language);
        }

        var vary = httpContext.Response.Headers.Vary;
        if (!vary.Any(value => value is not null && value.Contains(HeaderNames.AcceptLanguage, StringComparison.OrdinalIgnoreCase)))
        {
            httpContext.Response.Headers.Append(HeaderNames.Vary, HeaderNames.AcceptLanguage);
        }

        return language;
    }
}
