using System.Globalization;
using Frappe.SharedKernel.Validation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace Frappe.Platform.Web.Problems;

/// <summary>
/// The one problem shape every endpoint answers with. It translates at execution time, when the request locale is
/// known, so endpoints keep returning <c>result.ToProblem()</c>. Public only so endpoints can name it in a typed
/// <c>Results&lt;...&gt;</c>; build it with <see cref="ProblemResults.ToProblem"/>.
/// </summary>
public sealed class LocalizedProblemResult : IResult, IStatusCodeHttpResult
{
    public const string ValidationType = "urn:frappe:problem:validation";
    public const string ValidationTitleKey = "platform.problem.validation";

    private readonly IReadOnlyList<Error> _errors;

    internal LocalizedProblemResult(IReadOnlyList<Error> errors)
    {
        _errors = errors;
        StatusCode = errors.Select(e => e.Kind).Distinct().Count() == 1
            ? ProblemResults.StatusOf(errors[0].Kind)
            : StatusCodes.Status422UnprocessableEntity;
    }

    public int StatusCode { get; }

    int? IStatusCodeHttpResult.StatusCode => StatusCode;

    public Task ExecuteAsync(HttpContext httpContext)
    {
        var messages = ProblemText.For(httpContext);
        var problem = new ProblemDetails { Status = StatusCode };

        if (_errors.Count == 1)
        {
            var error = _errors[0];
            var message = messages.Translate(error.Code, error.Message);
            problem.Type = $"urn:frappe:problem:{error.Code}";
            problem.Title = message;
            problem.Detail = message;
            problem.Extensions["code"] = error.Code;
        }
        else
        {
            problem.Type = ValidationType;
            problem.Title = messages.Translate(ValidationTitleKey, "One or more rules were violated.");
        }

        problem.Extensions["errors"] = _errors.Select(e => new { e.Code, e.Field, Message = messages.Translate(e.Code, e.Message) });

        // What an error chose to disclose (e.g. the owner id after a proven password) becomes a top-level member.
        foreach (var (key, value) in _errors.Where(e => e.Metadata is not null).SelectMany(e => e.Metadata!))
            problem.Extensions[key] = value;

        if (StatusCode is StatusCodes.Status429TooManyRequests or StatusCodes.Status503ServiceUnavailable
            && _errors.Max(e => e.RetryAfter) is { } wait)
        {
            httpContext.Response.Headers[HeaderNames.RetryAfter] =
                Math.Max(1L, (long)Math.Ceiling(wait.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        }

        return TypedResults.Problem(problem).ExecuteAsync(httpContext);
    }
}
