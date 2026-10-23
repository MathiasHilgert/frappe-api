using Frappe.SharedKernel.Validation;
using Microsoft.AspNetCore.Http;

namespace Frappe.Platform.Web.Problems;

public static class ProblemResults
{
    /// <summary>Domain rule violations become a 422 problem with one entry per broken rule.</summary>
    public static IResult ToProblem(this Result result)
    {
        if (result.IsSuccess)
            throw new InvalidOperationException("A successful result is not a problem.");

        return TypedResults.Problem(
            title: "One or more domain rules were violated.",
            statusCode: StatusCodes.Status422UnprocessableEntity,
            type: "urn:frappe:problem:validation",
            extensions: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["errors"] = result.Errors.Select(e => new { e.Code, e.Message, e.Field }),
            });
    }
}
