using System.Diagnostics;
using Frappe.Kernel;
using Frappe.Kernel.Localization;
using Frappe.Platform.Http.Localization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Platform.Http.Problems;

/// <summary>
/// The one ErrorOr → RFC 9457 Problem Details mapper: the status and <c>type</c> come from the error type, every
/// error becomes an <c>errors[]</c> entry in the request's locale, and the response is never stored by a cache.
/// </summary>
public static class ProblemMapper
{
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

        (int status, string type) = errors[0] switch
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
            { Type: ErrorType.Conflict } => (StatusCodes.Status409Conflict, ProblemTypes.Conflict),
            { NumericType: ProblemTypes.MalformedRequestErrorType } => (
                StatusCodes.Status400BadRequest,
                ProblemTypes.MalformedRequest
            ),
            _ => (StatusCodes.Status500InternalServerError, ProblemTypes.Internal),
        };
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
        Locale locale = RequestLocale.Of(context.Request);
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

    private static ProblemError ToEntry(Error error, ErrorTexts texts, Locale locale)
    {
        var args = error
            .Metadata?.Where(static entry =>
                !string.Equals(entry.Key, Errors.PointerKey, StringComparison.Ordinal)
            )
            .ToDictionary(StringComparer.Ordinal);
        return new ProblemError(
            error.Code,
            error.Metadata?.GetValueOrDefault(Errors.PointerKey) as string,
            texts.Render(error, locale, args),
            args is { Count: > 0 } ? args : null
        );
    }
}
