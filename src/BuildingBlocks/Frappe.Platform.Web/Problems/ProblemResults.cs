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
    /// <remarks>A 429 sends <c>Retry-After</c> (whole seconds, rounded up) from the longest <see cref="Error.RetryAfter"/>.</remarks>
    public static IResult ToProblem(this Result result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.IsSuccess)
            throw new InvalidOperationException("A successful result is not a problem.");

        return new LocalizedProblemResult(result.Errors);
    }

    /// <summary>
    /// <paramref name="error"/>'s message in the request locale, looked up by its code and falling back to
    /// <c>Error.Message</c>; for endpoints that shape their own problem body instead of calling <c>ToProblem</c>.
    /// </summary>
    public static string LocalizedMessage(this HttpContext context, Error error)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(error);
        return ProblemText.For(context).Translate(error.Code, error.Message);
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
