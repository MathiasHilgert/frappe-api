using Frappe.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Frappe.Http;

/// <summary>
/// Cross-site request forgery defence for browser credentials: an unsafe request (<c>POST</c>, <c>PUT</c>,
/// <c>PATCH</c>, <c>DELETE</c>) that carries a credential the browser attaches on its own (a cookie) must come from a
/// console origin (<see cref="ConsoleOriginOptions"/>), named in its <c>Origin</c> header; otherwise it is answered
/// <c>403</c> <see cref="OriginForbiddenCode"/> before any endpoint runs. A request without such a
/// credential (anonymous, or authenticated by a header a page cannot attach across sites without passing CORS) is never
/// checked. Which requests carry one is the authentication's to say. Every refusal is counted by why
/// (<see cref="ConsoleOriginMetrics.RefusedMetric"/>), never with the origin it named.
/// </summary>
public static class ConsoleOriginCheck
{
    extension(IApplicationBuilder app)
    {
        /// <summary>
        /// Adds the check; <paramref name="carriesBrowserCredential"/> says whether a request carries a credential the browser
        /// attaches on its own. Call it after authentication and before authorization, so its answer outweighs a 401.
        /// </summary>
        public IApplicationBuilder UseConsoleOriginCheck(Func<HttpContext, bool> carriesBrowserCredential)
        {
            ArgumentNullException.ThrowIfNull(app);
            ArgumentNullException.ThrowIfNull(carriesBrowserCredential);
            return app.Use((httpContext, next) =>
            {
                var request = httpContext.Request;
                if (!IsUnsafe(request.Method)
                    || !carriesBrowserCredential(httpContext)
                    || httpContext.RequestServices.GetRequiredService<IOptions<ConsoleOriginOptions>>().Value.Allows(request.Headers.Origin))
                {
                    return next(httpContext);
                }

                // Counting is best effort: a host without the metrics registered still answers the 403, uncounted.
                httpContext.RequestServices.GetService<ConsoleOriginMetrics>()
                    ?.Refused(request.Headers.Origin.Count == 0 ? OriginRefusal.Missing : OriginRefusal.NotAllowed);
                return ErrorsProblemHttpResult.Of(new ForbiddenError(OriginForbiddenCode)).ExecuteAsync(httpContext);
            });
        }
    }

    /// <summary>An unsafe request carried a browser credential without a console <c>Origin</c> (<see cref="ConsoleOriginCheck"/>).</summary>
    public const string OriginForbiddenCode = "http.origin.forbidden";

    private static bool IsUnsafe(string method) =>
        HttpMethods.IsPost(method) || HttpMethods.IsPut(method) || HttpMethods.IsPatch(method) || HttpMethods.IsDelete(method);
}
