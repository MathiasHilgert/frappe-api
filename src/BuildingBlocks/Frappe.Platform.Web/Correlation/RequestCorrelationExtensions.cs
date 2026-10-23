using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Platform.Web.Correlation;

public static class RequestCorrelationExtensions
{
    /// <summary>Registers problem details that carry the request id as a <c>requestId</c> extension.</summary>
    public static IServiceCollection AddRequestCorrelation(this IServiceCollection services) =>
        services.AddProblemDetails(options => options.CustomizeProblemDetails = ctx =>
            ctx.ProblemDetails.Extensions["requestId"] = ctx.HttpContext.TraceIdentifier);

    /// <summary>Place early, before the exception handler, so failures are correlated too.</summary>
    public static IApplicationBuilder UseRequestCorrelation(this IApplicationBuilder app) =>
        app.UseMiddleware<RequestCorrelationMiddleware>();
}
