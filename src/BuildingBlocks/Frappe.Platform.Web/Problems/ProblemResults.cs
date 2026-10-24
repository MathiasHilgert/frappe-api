using Frappe.SharedKernel.Validation;
using Microsoft.AspNetCore.Http;

namespace Frappe.Platform.Web.Problems;

public static class ProblemResults
{
    /// <summary>
    /// A failed result as an RFC 9457 problem. The status follows the errors' <see cref="ErrorKind"/> (mixed kinds
    /// answer 422); <c>type</c> is <c>urn:frappe:problem:&lt;code&gt;</c> for one error and
    /// <c>urn:frappe:problem:validation</c> for several; <c>title</c> and every <c>errors[].message</c> are
    /// translated into the request locale by error code, falling back to the error's own message. One error also
    /// sets <c>detail</c> (same text) and <c>code</c>. Every <see cref="Error.Metadata"/> entry is a top-level member.
    /// </summary>
    /// <remarks>A 429 or 503 sends <c>Retry-After</c> (whole seconds, rounded up, at least 1) from the longest <see cref="Error.RetryAfter"/>.</remarks>
    public static LocalizedProblemResult ToProblem(this Result result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.IsSuccess)
            throw new InvalidOperationException("A successful result is not a problem.");

        return new LocalizedProblemResult(result.Errors);
    }

    /// <inheritdoc cref="ToProblem(Result)"/>
    public static LocalizedProblemResult ToProblem<T>(this Result<T> result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.IsSuccess)
            throw new InvalidOperationException("A successful result is not a problem.");

        return new LocalizedProblemResult(result.Errors);
    }

    internal static int StatusOf(ErrorKind kind) => kind switch
    {
        ErrorKind.NotFound => StatusCodes.Status404NotFound,
        ErrorKind.Conflict => StatusCodes.Status409Conflict,
        ErrorKind.TooManyRequests => StatusCodes.Status429TooManyRequests,
        ErrorKind.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorKind.Forbidden => StatusCodes.Status403Forbidden,
        ErrorKind.Unavailable => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status422UnprocessableEntity,
    };
}
