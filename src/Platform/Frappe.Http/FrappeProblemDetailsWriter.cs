using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using static Frappe.Http.ResultHttpExtensions;

namespace Frappe.Http;

/// <summary>
/// Writes every problem with the Frappé contract (<see cref="FrappeProblemDetails.Customize"/>), always as
/// <c>application/problem+json</c>, with <c>no-store</c>:
/// <list type="bullet">
/// <item>the framework's writer skips a client whose <c>Accept</c> rules out JSON; such a client still gets the same
/// problem, since a bodiless 404 could be kept by a cache;</item>
/// <item>the problem is serialized as a plain <see cref="ProblemDetails"/>: a body the framework could not read comes
/// as an <see cref="HttpValidationProblemDetails"/>, whose own <c>errors</c> (System.Text.Json's English text and
/// .NET type names) would otherwise be written next to Frappé's.</item>
/// </list>
/// </summary>
internal sealed class FrappeProblemDetailsWriter : IProblemDetailsWriter
{
    public bool CanWrite(ProblemDetailsContext context) => true;

    public ValueTask WriteAsync(ProblemDetailsContext context)
    {
        FrappeProblemDetails.Customize(context);
        var httpContext = context.HttpContext;
        return new ValueTask(httpContext.Response.WriteAsJsonAsync<ProblemDetails>(
            context.ProblemDetails, ApplicationJson(httpContext), "application/problem+json", httpContext.RequestAborted));
    }
}
