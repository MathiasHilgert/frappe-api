using System.Globalization;
using Frappe.Results;
using Microsoft.AspNetCore.Http;
using static Frappe.Http.ResultHttpExtensions;

namespace Frappe.Http;

/// <summary>
/// An RFC 9457 Problem Details response listing every error of a failed result, written by the framework with the
/// Frappé contract (<see cref="FrappeProblemDetails"/>): the most decisive error decides the category, and every error
/// is listed with its code, parameters and text in the requested language. An unavailable dependency also says when to
/// try again (<c>Retry-After</c>, <see cref="UnavailableRetryAfterSeconds"/>), a rate-limited caller is told the longest
/// wait its errors carry (<c>Retry-After</c>, <see cref="RateLimitedError.RetryAfterSeconds"/>), and an unauthenticated caller is told the
/// scheme to authenticate with (<c>WWW-Authenticate</c>, <see cref="AuthenticationScheme"/>, required on every 401 by
/// RFC 9110). Built by <see cref="ResultHttpExtensions"/>.
/// </summary>
public sealed class ErrorsProblemHttpResult : IResult
{
    /// <summary>
    /// The <c>Retry-After</c> of an unavailable dependency, in seconds: long enough for a retry or a breaker to settle
    /// and for a client not to hammer it, short enough for a person still on the page.
    /// </summary>
    public const int UnavailableRetryAfterSeconds = 30;

    /// <summary>
    /// The scheme a 401 challenges with. Every client can present the session token as a bearer token; a browser that
    /// holds it in a cookie sends it without being asked.
    /// </summary>
    public const string AuthenticationScheme = "Bearer";

    /// <summary>
    /// The <c>Retry-After</c> of a rate-limited answer that names no wait of its own, in seconds: a fallback that only a
    /// mapping bug could reach, long enough not to invite a retry storm.
    /// </summary>
    public const int DefaultRateLimitedRetryAfterSeconds = 60;

    private readonly IReadOnlyList<Error> _errors;

    internal ErrorsProblemHttpResult(IReadOnlyList<Error> errors) => _errors = errors;

    /// <summary>
    /// The problem of <paramref name="errors"/>, for an adapter that answers without a use case (an authentication
    /// challenge, a refused origin): the same document, status and headers as a failed result.
    /// </summary>
    public static ErrorsProblemHttpResult Of(params Error[] errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentOutOfRangeException.ThrowIfZero(errors.Length);
        return new ErrorsProblemHttpResult(errors);
    }

    /// <summary>
    /// The longest wait among <paramref name="errors"/>' rate limits, or <see cref="DefaultRateLimitedRetryAfterSeconds"/>
    /// when none carries one, never a failure.
    /// </summary>
    internal static int RateLimitedRetryAfterSeconds(IEnumerable<Error> errors) =>
        errors.OfType<RateLimitedError>().Select(e => e.RetryAfterSeconds).DefaultIfEmpty(DefaultRateLimitedRetryAfterSeconds).Max();

    /// <inheritdoc/>
    public Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var decisive = _errors.MaxBy(e => ShapeOf(e).Precedence)!;
        var category = ShapeOf(decisive).Category;
        if (category == ProblemCategory.Unavailable)
        {
            httpContext.Response.Headers.RetryAfter = UnavailableRetryAfterSeconds.ToString(CultureInfo.InvariantCulture);
        }
        else if (category == ProblemCategory.TooManyRequests)
        {
            httpContext.Response.Headers.RetryAfter = RateLimitedRetryAfterSeconds(_errors).ToString(CultureInfo.InvariantCulture);
        }
        else if (category == ProblemCategory.Unauthenticated)
        {
            httpContext.Response.Headers.WWWAuthenticate = AuthenticationScheme;
        }

        return FrappeProblemDetails.WriteAsync(
            httpContext,
            new ProblemOccurrence(category, [.. _errors.Select(e => new ProblemError(e.Code, e.Parameters, null))]));
    }
}
