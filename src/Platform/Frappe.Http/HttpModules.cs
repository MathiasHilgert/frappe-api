using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Http;

/// <summary>Composes the HTTP modules a host lists into the API.</summary>
public static class HttpModules
{
    extension(IServiceCollection services)
    {
        /// <summary>Registers the services of every module in <paramref name="modules"/>, in order.</summary>
        public IServiceCollection AddHttpModules(IEnumerable<IHttpModule> modules)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(modules);
            foreach (var module in modules)
            {
                module.AddServices(services);
            }

            return services;
        }
    }

    extension(IEndpointRouteBuilder endpoints)
    {
        /// <summary>
        /// Maps <paramref name="modules"/> under <see cref="V1Prefix"/> with the conventions every endpoint shares: the console's CORS
        /// policy, HEAD wherever GET is answered, the negotiated language, the anonymous rate limit, and the <c>429</c> it may
        /// answer declared once for all of them.
        /// </summary>
        public RouteGroupBuilder MapHttpModules(IEnumerable<IHttpModule> modules)
        {
            ArgumentNullException.ThrowIfNull(endpoints);
            ArgumentNullException.ThrowIfNull(modules);
            var v1 = endpoints.MapGroup(V1Prefix)
                .RequireCors(HttpRegistration.ConsolePolicy)
                .AnswerHead()
                .NegotiateRequestLanguage()
                .RequireRateLimiting(HttpRegistration.AnonymousPolicy)
                .ProducesProblem(StatusCodes.Status429TooManyRequests);
            foreach (var module in modules)
            {
                module.MapEndpoints(v1);
            }

            return v1;
        }
    }

    /// <summary>The path every module's endpoints live under.</summary>
    public const string V1Prefix = "/v1";
}
