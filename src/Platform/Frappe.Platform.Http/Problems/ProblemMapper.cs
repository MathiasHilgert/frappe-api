using System.Diagnostics;
using Frappe.Kernel;
using Frappe.Platform.Http.Localization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Frappe.Platform.Http.Problems;

/// <summary>
/// The one ErrorOr → RFC 9457 Problem Details mapper: the status and <c>type</c> come from the error type, every
/// error becomes an <c>errors[]</c> entry in the request's locale, and the response is never stored by a cache. A
/// server error never shows its producer's errors: it carries only <c>platform.request.failed</c> and logs them.
/// </summary>
public static class ProblemMapper
{
    private const string BearerChallenge = "Bearer";

    /// <summary>Maps the errors of a failed result to the house problem response.</summary>
    /// <param name="errors">The errors, at least one; the first decides the status.</param>
    /// <param name="context">The current request, for its locale and services.</param>
    /// <returns>The problem result to return from the endpoint.</returns>
    public static ProblemHttpResult ToProblem(this IReadOnlyList<Error> errors, HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentNullException.ThrowIfNull(context);
        if (errors.Count == 0)
        {
            throw new ArgumentException("A problem needs at least one error.", nameof(errors));
        }

        (int status, string type) = StatusOf(errors[0]);
        if (status == StatusCodes.Status500InternalServerError)
        {
            Log.ServerErrorReturned(
                context
                    .RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger(typeof(ProblemMapper)),
                context.Request.Path,
                string.Join(", ", errors.Select(static error => error.Code))
            );
            return TypedResults.Problem(Create(context, status, type, [PlatformErrors.Failed]));
        }

        if (status == StatusCodes.Status401Unauthorized)
        {
            context.Response.Headers.WWWAuthenticate = BearerChallenge;
        }

        return TypedResults.Problem(Create(context, status, type, errors));
    }

    internal static ProblemDetails Create(
        HttpContext context,
        int status,
        string type,
        IEnumerable<Error> errors
    )
    {
        ErrorTexts texts = context.RequestServices.GetRequiredService<ErrorTexts>();
        ErrorLocale locale = RequestLocale.Of(context.Request);
        return new ProblemDetails
        {
            Status = status,
            Type = type,
            Title = ProblemTypes.TitleOf(type),
            Extensions =
            {
                ["errors"] = errors.Select(error => ToEntry(error, texts, locale)).ToArray(),
            },
        };
    }

    // Applied to every problem the app writes, whoever builds it.
    internal static void Customize(ProblemDetailsContext context)
    {
        context.ProblemDetails.Instance = context.HttpContext.Request.Path;
        context.ProblemDetails.Extensions["traceId"] =
            Activity.Current?.TraceId.ToHexString() ?? context.HttpContext.TraceIdentifier;
        context.HttpContext.Response.Headers.CacheControl = "no-store";
    }

    private static (int Status, string Type) StatusOf(Error error) =>
        error switch
        {
            { Type: ErrorType.Validation } => (
                StatusCodes.Status422UnprocessableEntity,
                ProblemTypes.Validation
            ),
            { Type: ErrorType.NotFound } => (StatusCodes.Status404NotFound, ProblemTypes.NotFound),
            { Type: ErrorType.Forbidden } => (
                StatusCodes.Status403Forbidden,
                ProblemTypes.Forbidden
            ),
            { Type: ErrorType.Unauthorized } => (
                StatusCodes.Status401Unauthorized,
                ProblemTypes.Unauthorized
            ),
            { Type: ErrorType.Conflict } => (StatusCodes.Status409Conflict, ProblemTypes.Conflict),
            { NumericType: ProblemTypes.MalformedRequestErrorType } => (
                StatusCodes.Status400BadRequest,
                ProblemTypes.MalformedRequest
            ),
            _ => (StatusCodes.Status500InternalServerError, ProblemTypes.Internal),
        };

    // A description may hold internals, so a code without a text gets its type's generic text instead.
    private static Error GenericOf(Error error) =>
        error switch
        {
            { Type: ErrorType.Validation } => PlatformErrors.Invalid,
            { Type: ErrorType.NotFound } => PlatformErrors.NotFound,
            { Type: ErrorType.Forbidden } => PlatformErrors.Forbidden,
            { Type: ErrorType.Unauthorized } => PlatformErrors.Unauthorized,
            { Type: ErrorType.Conflict } => PlatformErrors.Conflict,
            { NumericType: ProblemTypes.MalformedRequestErrorType } => PlatformErrors.Malformed,
            _ => PlatformErrors.Failed,
        };

    private static ProblemError ToEntry(Error error, ErrorTexts texts, ErrorLocale locale)
    {
        var args = error
            .Metadata?.Where(static entry =>
                !string.Equals(entry.Key, Errors.PointerKey, StringComparison.Ordinal)
            )
            .ToDictionary(StringComparer.Ordinal);
        Error generic = GenericOf(error);
        return new ProblemError(
            error.Code,
            error.Metadata?.GetValueOrDefault(Errors.PointerKey) as string,
            texts.Render(error.Code, locale, args)
                ?? texts.Render(generic.Code, locale)
                ?? generic.Description,
            args is { Count: > 0 } ? args : null
        );
    }
}
