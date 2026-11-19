using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Frappe.Http;

/// <summary>
/// Wires the Problem Details contract (<see cref="FrappeProblemDetails"/>) into a host: the writer that shapes every
/// problem, and the middleware that turns a failed or bodiless response into one.
/// </summary>
internal static class ProblemDetailsRegistration
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers the framework's problem service with the Frappé contract and <see cref="FrappeProblemDetailsWriter"/> ahead of the
        /// framework's own writer, so every problem, whoever raises it, is written by Frappé.
        /// </summary>
        public IServiceCollection AddFrappeProblemDetails()
        {
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IProblemDetailsWriter, FrappeProblemDetailsWriter>());
            return services.AddProblemDetails(options => options.CustomizeProblemDetails = FrappeProblemDetails.Customize);
        }
    }

    extension(IApplicationBuilder app)
    {
        /// <summary>
        /// Answers what fails as Problem Details: an exception the middleware catches (a request the framework rejects
        /// keeps its <c>4xx</c>, a database outage is a <c>503</c>, anything else a <c>500</c>) and any <c>4xx</c>
        /// answered without a body.
        /// </summary>
        public IApplicationBuilder UseFrappeProblemDetails() => app
            .UseExceptionHandler(new ExceptionHandlerOptions
            {
                StatusCodeSelector = exception => exception switch
                {
                    BadHttpRequestException rejected => rejected.StatusCode,
                    _ when DatabaseOutage.Caused(exception) => StatusCodes.Status503ServiceUnavailable,
                    _ => StatusCodes.Status500InternalServerError,
                },
                ExceptionHandler = WriteExceptionProblemAsync,
            })
            .UseStatusCodePages(StatusCodeProblems.WriteAsync);
    }

    /// <summary>
    /// The problem of an exception the middleware caught: a database outage (<see cref="DatabaseOutage"/>, read from the
    /// exception itself, never from the status) tells the client when to try again; any other exception is the platform
    /// problem of the status <c>StatusCodeSelector</c> chose, a <c>503</c> included.
    /// </summary>
    private static Task WriteExceptionProblemAsync(HttpContext httpContext)
    {
        if (httpContext.Features.Get<IExceptionHandlerFeature>()?.Error is { } exception && DatabaseOutage.Caused(exception))
        {
            httpContext.Response.Headers.RetryAfter = HttpRegistration.DatabaseUnavailableRetryAfterSeconds.ToString(CultureInfo.InvariantCulture);
            return PlatformProblem.WriteAsync(
                httpContext,
                PlatformProblem.Of(
                    ProblemCategory.Unavailable,
                    HttpRegistration.DatabaseUnavailableCode,
                    new Dictionary<string, object?> { ["retryAfter"] = HttpRegistration.DatabaseUnavailableRetryAfterSeconds }));
        }

        return PlatformProblem.WriteAsync(httpContext, PlatformProblem.ForStatus(httpContext, httpContext.Response.StatusCode));
    }
}
