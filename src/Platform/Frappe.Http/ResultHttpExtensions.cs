using System.Text.Json;
using Frappe.Localization;
using Frappe.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using HttpJsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace Frappe.Http;

/// <summary>
/// Translates application results into HTTP responses. The only place that knows status codes,
/// and the place where error codes become text in the language the client asked for.
/// </summary>
public static class ResultHttpExtensions
{
    extension<T>(Result<T> result)
    {
        /// <summary>
        /// The one way an endpoint answers a result: projects a success with <paramref name="onSuccess"/>, or answers an RFC 9457
        /// Problem Details document listing every error. The endpoint declares both as a <see cref="Results{TResult1, TResult2}"/>
        /// union; text is rendered only in the requested language, otherwise only codes are sent.
        /// </summary>
        public Results<TSuccess, ErrorsProblemHttpResult> ToTypedHttpResult<TSuccess>(Func<T, TSuccess> onSuccess)
            where TSuccess : IResult
        {
            ArgumentNullException.ThrowIfNull(result);
            ArgumentNullException.ThrowIfNull(onSuccess);

            return result is Failure failure ? new ErrorsProblemHttpResult(failure.Errors) : onSuccess(result.Value);
        }
    }

    /// <summary>
    /// Renders texts for one response in the negotiated language (English when the request was never negotiated) and
    /// remembers every language the texts were written in.
    /// </summary>
    internal sealed class TextRenderer(ErrorMessageCatalog? catalog, LanguageTag? requestedLanguage)
    {
        private readonly List<LanguageTag> _writtenIn = [];

        public static TextRenderer For(HttpContext httpContext) => new(
            httpContext.RequestServices.GetService<ErrorMessageCatalog>(),
            httpContext.Features.Get<RequestLanguage>()?.Tag);

        public string? Render(string key, IReadOnlyDictionary<string, object?> parameters)
        {
            if (catalog is null)
            {
                return null;
            }

            var rendered = catalog.Render(key, requestedLanguage ?? LanguageFallback.English, parameters);
            if (rendered is not { } value)
            {
                return null;
            }

            if (!_writtenIn.Contains(value.Language))
            {
                _writtenIn.Add(value.Language);
            }

            return value.Text;
        }

        /// <summary>Names in <c>Content-Language</c> every language the response was written in, if any.</summary>
        public void DeclareLanguages(HttpResponse response)
        {
            if (_writtenIn.Count > 0)
            {
                response.Headers.ContentLanguage = string.Join(", ", _writtenIn.Select(language => language.Value));
            }
        }
    }

    /// <summary>Reuses the application's JSON settings (naming policy, NodaTime converters).</summary>
    internal static JsonSerializerOptions ApplicationJson(HttpContext httpContext) =>
        httpContext.RequestServices.GetService<IOptions<HttpJsonOptions>>()?.Value.SerializerOptions
        ?? JsonSerializerOptions.Web;

    /// <summary>
    /// How each error type is told over HTTP: its <see cref="ProblemCategory"/> (status, title and type). When one
    /// response carries several types, the one with the highest precedence decides: a missing resource outweighs a
    /// conflict, which outweighs a broken rule, which outweighs an invalid value. An unavailable dependency outweighs them
    /// all: whatever else failed, the same request may succeed later. A caller that may not act outweighs every error that
    /// would describe the request (an invalid value, a broken rule, a conflict), so a refusal never leaks them; a missing
    /// resource still outweighs it, so a refusal never confirms that a resource exists. A caller that tried too often
    /// outweighs every error that would describe the request, a missing resource included, so a throttled caller learns
    /// nothing but when to try again; an unavailable dependency still outweighs it. A caller that did not prove who
    /// they are outweighs everything: nothing else is told to an unknown caller. The order in which errors were collected
    /// never matters.
    /// </summary>
    internal sealed record ProblemShape(ProblemCategory Category, int Precedence);

    // The weakest: a request that fails for any other reason is told that reason first, and fixing it changes the request,
    // which the caller then sends against the state it reads again anyway.
    private static readonly ProblemShape PreconditionFailedShape = new(ProblemCategory.PreconditionFailed, 0);
    private static readonly ProblemShape InvalidValueShape = new(ProblemCategory.InvalidValue, 1);
    private static readonly ProblemShape RuleViolationShape = new(ProblemCategory.RuleViolation, 2);
    private static readonly ProblemShape ConflictShape = new(ProblemCategory.Conflict, 3);
    private static readonly ProblemShape ForbiddenShape = new(ProblemCategory.Forbidden, 4);
    private static readonly ProblemShape NotFoundShape = new(ProblemCategory.NotFound, 5);
    private static readonly ProblemShape RateLimitedShape = new(ProblemCategory.TooManyRequests, 6);
    private static readonly ProblemShape UnavailableShape = new(ProblemCategory.Unavailable, 7);
    private static readonly ProblemShape UnauthenticatedShape = new(ProblemCategory.Unauthenticated, 8);

    /// <summary>
    /// <see cref="Error"/> is a closed hierarchy, so this switch needs no default arm: a new kind of error that is not
    /// mapped here does not compile (CS8509), instead of failing at runtime.
    /// </summary>
    internal static ProblemShape ShapeOf(Error error) => error switch
    {
        InvalidValueError => InvalidValueShape,
        RuleViolationError => RuleViolationShape,
        ConflictError => ConflictShape,
        NotFoundError => NotFoundShape,
        UnavailableError => UnavailableShape,
        RateLimitedError => RateLimitedShape,
        ForbiddenError => ForbiddenShape,
        UnauthenticatedError => UnauthenticatedShape,
        PreconditionFailedError => PreconditionFailedShape,
    };
}
