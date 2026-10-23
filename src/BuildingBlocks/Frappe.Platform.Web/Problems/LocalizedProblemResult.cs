using System.Globalization;
using Frappe.Platform.Localization;
using Frappe.SharedKernel.Validation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace Frappe.Platform.Web.Problems;

/// <summary>Translates at execution time, when the request locale is known, so endpoints keep returning <c>result.ToProblem()</c>.</summary>
internal sealed class LocalizedProblemResult(IReadOnlyList<Error> errors, TimeSpan? retryAfter) : IResult, IStatusCodeHttpResult
{
    public const string ValidationType = "urn:frappe:problem:validation";
    public const string ValidationTitleKey = "platform.problem.validation";

    public int StatusCode { get; } = errors.Select(e => e.Kind).Distinct().Count() == 1
        ? ProblemResults.StatusOf(errors[0].Kind)
        : StatusCodes.Status422UnprocessableEntity;

    int? IStatusCodeHttpResult.StatusCode => StatusCode;

    public Task ExecuteAsync(HttpContext httpContext)
    {
        var messages = ProblemText.For(httpContext);
        var single = errors.Count == 1;
        var problem = new ProblemDetails
        {
            Status = StatusCode,
            Type = single ? $"urn:frappe:problem:{errors[0].Code}" : ValidationType,
            Title = single
                ? messages.Translate(errors[0].Code, errors[0].Message)
                : messages.Translate(ValidationTitleKey, "One or more rules were violated."),
            Extensions =
            {
                ["errors"] = errors.Select(e => new { e.Code, e.Field, Message = messages.Translate(e.Code, e.Message) }),
            },
        };

        if (StatusCode == StatusCodes.Status429TooManyRequests && retryAfter is { } wait)
            httpContext.Response.Headers[HeaderNames.RetryAfter] =
                ((long)Math.Ceiling(wait.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

        return TypedResults.Problem(problem).ExecuteAsync(httpContext);
    }
}
