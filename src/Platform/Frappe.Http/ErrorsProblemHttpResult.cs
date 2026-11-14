using System.Globalization;
using Frappe.Results;
using Microsoft.AspNetCore.Http;
using static Frappe.Http.ResultHttpExtensions;

namespace Frappe.Http;

/// <summary>
/// An RFC 9457 Problem Details response listing every error of a failed result, written by the framework with the
/// Frappé contract (<see cref="FrappeProblemDetails"/>): the most decisive error decides the category, and every error
/// is listed with its code, parameters and text in the requested language. An unavailable dependency also says when to
/// try again (<c>Retry-After</c>, <see cref="UnavailableRetryAfterSeconds"/>). Built by <see cref="ResultHttpExtensions"/>.
/// </summary>
public sealed class ErrorsProblemHttpResult : IResult
{
    /// <summary>
    /// The <c>Retry-After</c> of an unavailable dependency, in seconds: long enough for a retry or a breaker to settle
    /// and for a client not to hammer it, short enough for a person still on the page.
    /// </summary>
    public const int UnavailableRetryAfterSeconds = 30;

    private readonly IReadOnlyList<Error> _errors;

    internal ErrorsProblemHttpResult(IReadOnlyList<Error> errors) => _errors = errors;

    public Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var decisive = _errors.MaxBy(e => ShapeOf(e).Precedence)!;
        if (ShapeOf(decisive).Category == ProblemCategory.Unavailable)
        {
            httpContext.Response.Headers.RetryAfter = UnavailableRetryAfterSeconds.ToString(CultureInfo.InvariantCulture);
        }

        return FrappeProblemDetails.WriteAsync(
            httpContext,
            new ProblemOccurrence(ShapeOf(decisive).Category, [.. _errors.Select(e => new ProblemError(e.Code, e.Parameters, null))]));
    }
}
