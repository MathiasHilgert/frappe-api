using Frappe.Platform.Localization;
using Frappe.Platform.Web.Problems;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Platform.Web.Correlation;

public static class RequestCorrelationExtensions
{
    /// <summary>
    /// Registers problem details that carry the request id as a <c>requestId</c> extension, and maps malformed
    /// requests to their own 4xx problem instead of a 500. Framework problems (status pages, bad requests, 500s)
    /// get their title from the <c>platform.problem.&lt;status&gt;</c> catalogue entry in the request locale.
    /// </summary>
    public static IServiceCollection AddRequestCorrelation(this IServiceCollection services)
    {
        services.AddExceptionHandler<BadRequestExceptionHandler>();
        services.AddExceptionHandler<UnavailableExceptionHandler>();
        services.AddMessageCatalog(typeof(RequestCorrelationExtensions).Assembly);
        return services.AddProblemDetails(options => options.CustomizeProblemDetails = ctx =>
        {
            ctx.ProblemDetails.Extensions["requestId"] = ctx.HttpContext.TraceIdentifier;
            var ours = ctx.ProblemDetails.Type?.StartsWith("urn:frappe:", StringComparison.Ordinal) == true;
            if (!ours && ctx.ProblemDetails.Status is { } status)
            {
                ctx.ProblemDetails.Title = ProblemText.For(ctx.HttpContext)
                    .Translate($"platform.problem.{status}", ctx.ProblemDetails.Title ?? "");
            }
        });
    }

    /// <summary>Place early, before the exception handler, so failures are correlated too.</summary>
    public static IApplicationBuilder UseRequestCorrelation(this IApplicationBuilder app) =>
        app.UseMiddleware<RequestCorrelationMiddleware>();
}
