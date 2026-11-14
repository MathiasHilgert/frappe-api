using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.Net.Http.Headers;

namespace Frappe.Http;

/// <summary>
/// The problems no use case produces (a request the framework cannot read, an unknown route or method, a rejected or
/// failed request), with the same contract as <see cref="ErrorsProblemHttpResult"/>: one error with a platform code.
/// They follow <c>Accept-Language</c> like every other problem, even when no endpoint negotiated the language.
/// </summary>
internal static class PlatformProblem
{
    private static readonly IReadOnlyDictionary<string, object?> NoParameters = new Dictionary<string, object?>();

    /// <summary>Negotiates the language, then answers <paramref name="occurrence"/>.</summary>
    public static Task WriteAsync(HttpContext httpContext, ProblemOccurrence occurrence)
    {
        LanguageNegotiation.Apply(httpContext);
        return FrappeProblemDetails.WriteAsync(httpContext, occurrence);
    }

    /// <summary>One platform error of <paramref name="category"/>.</summary>
    public static ProblemOccurrence Of(ProblemCategory category, string code, IReadOnlyDictionary<string, object?>? parameters = null) =>
        new(category, [new ProblemError(code, parameters ?? NoParameters, null)]);

    /// <summary>
    /// The platform problem of a response status: an unknown route (<c>404</c>), a method the route does not answer
    /// (<c>405</c>, listing routing's <c>Allow</c>, which it only reads), a body too large (<c>413</c>), a body in a
    /// media type the endpoint does not read (<c>415</c>, with the one sent and the ones accepted), any <c>5xx</c> as
    /// unexpected, and a request the framework could not read as a bad request that keeps its status: <c>400</c>, or any
    /// other <c>4xx</c> answered without a body (a <c>408</c> for a body arriving too slowly).
    /// </summary>
    public static ProblemOccurrence ForStatus(HttpContext httpContext, int status) => status switch
    {
        StatusCodes.Status404NotFound => Of(ProblemCategory.NotFound, HttpRegistration.NotFoundCode),
        StatusCodes.Status405MethodNotAllowed => Of(
            ProblemCategory.MethodNotAllowed,
            HttpRegistration.MethodNotAllowedCode,
            new Dictionary<string, object?> { ["method"] = httpContext.Request.Method, ["allowed"] = AllowedMethods(httpContext.Response) }),
        StatusCodes.Status413PayloadTooLarge => Of(ProblemCategory.RequestTooLarge, HttpRegistration.RequestTooLargeCode),
        StatusCodes.Status415UnsupportedMediaType => Of(
            ProblemCategory.UnsupportedMediaType,
            HttpRegistration.UnsupportedMediaTypeCode,
            new Dictionary<string, object?> { ["contentType"] = httpContext.Request.ContentType, ["accepted"] = AcceptedMediaTypes(httpContext) }),
        >= 500 => Of(ProblemCategory.Unexpected, HttpRegistration.UnexpectedCode),
        >= 400 and < 500 => Of(ProblemCategory.BadRequest, HttpRegistration.BadRequestCode) with { Status = status },
        _ => Of(ProblemCategory.BadRequest, HttpRegistration.BadRequestCode),
    };

    /// <summary>
    /// The media types the endpoint reads (its <see cref="IAcceptsMetadata"/>); the exception handler clears the endpoint
    /// but keeps it in its feature. Frappé's bodies are JSON, the answer when no endpoint says otherwise.
    /// </summary>
    private static string[] AcceptedMediaTypes(HttpContext httpContext)
    {
        var endpoint = httpContext.GetEndpoint() ?? httpContext.Features.Get<IExceptionHandlerFeature>()?.Endpoint;
        string[] accepted = [.. (endpoint?.Metadata.GetOrderedMetadata<IAcceptsMetadata>() ?? [])
            .SelectMany(metadata => metadata.ContentTypes)
            .Distinct(StringComparer.OrdinalIgnoreCase)];
        return accepted.Length > 0 ? accepted : ["application/json"];
    }

    /// <summary>Routing's <c>Allow</c>; HEAD is in it wherever an endpoint opted in with <see cref="HeadRequests.AnswerHead"/>.</summary>
    private static string[] AllowedMethods(HttpResponse response) =>
        [.. response.Headers[HeaderNames.Allow]
            .SelectMany(value => (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))];
}
