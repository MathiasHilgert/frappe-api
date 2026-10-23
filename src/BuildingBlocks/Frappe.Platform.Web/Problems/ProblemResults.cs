using Frappe.SharedKernel.Validation;
using Microsoft.AspNetCore.Http;

namespace Frappe.Platform.Web.Problems;

public static class ProblemResults
{
    /// <summary>
    /// A failed result as an RFC 9457 problem. The status follows the errors' <see cref="ErrorKind"/> (mixed kinds
    /// answer 422); <c>type</c> is <c>urn:frappe:problem:&lt;code&gt;</c> for one error and
    /// <c>urn:frappe:problem:validation</c> for several; <c>title</c> and every <c>errors[].message</c> are
    /// translated into the request locale by error code, falling back to the error's own message.
    /// </summary>
    public static IResult ToProblem(this Result result) => result.ToProblem(retryAfter: null);

    /// <summary>As <see cref="ToProblem(Result)"/>, with a wait for 429 answers.</summary>
    /// <param name="result">The failed result.</param>
    /// <param name="retryAfter">Sent as <c>Retry-After</c> (whole seconds, rounded up) on a 429.</param>
    public static IResult ToProblem(this Result result, TimeSpan? retryAfter)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.IsSuccess)
            throw new InvalidOperationException("A successful result is not a problem.");

        return new LocalizedProblemResult(result.Errors, retryAfter);
    }

    internal static int StatusOf(ErrorKind kind) => kind switch
    {
        ErrorKind.NotFound => StatusCodes.Status404NotFound,
        ErrorKind.Conflict => StatusCodes.Status409Conflict,
        ErrorKind.TooManyRequests => StatusCodes.Status429TooManyRequests,
        ErrorKind.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorKind.Forbidden => StatusCodes.Status403Forbidden,
        _ => StatusCodes.Status422UnprocessableEntity,
    };
}
