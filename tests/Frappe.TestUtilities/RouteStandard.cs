using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;

namespace Frappe.TestUtilities;

/// <summary>
/// The security standard every mapped endpoint meets: exactly one rate-limit policy, and anonymous access only when
/// declared. Health, the OpenAPI document and Scalar are the declared internal endpoints and are exempt.
/// </summary>
public static class RouteStandard
{
    private static readonly string[] _internalPrefixes = ["/health", "/openapi/", "/scalar"];

    /// <summary>Lists every violation of the standard, one line per endpoint and rule.</summary>
    /// <param name="endpoints">The app's endpoint data source.</param>
    /// <param name="anonymousRoutes">The declared anonymous route patterns, such as <c>/v1/geo/countries</c>.</param>
    /// <returns>The violations; empty when every endpoint conforms.</returns>
    public static IReadOnlyList<string> Violations(
        EndpointDataSource endpoints,
        IReadOnlySet<string> anonymousRoutes
    )
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(anonymousRoutes);
        List<string> violations = [];
        foreach (RouteEndpoint endpoint in endpoints.Endpoints.OfType<RouteEndpoint>())
        {
            string route = "/" + (endpoint.RoutePattern.RawText ?? "").TrimStart('/');
            if (
                Array.Exists(
                    _internalPrefixes,
                    prefix => route.StartsWith(prefix, StringComparison.Ordinal)
                )
            )
            {
                continue;
            }

            EndpointMetadataCollection metadata = endpoint.Metadata;
            if (
                metadata.GetOrderedMetadata<EnableRateLimitingAttribute>().Count != 1
                || metadata.GetMetadata<DisableRateLimitingAttribute>() is not null
            )
            {
                violations.Add($"{route}: declares no single rate-limit policy");
            }

            if (
                metadata.GetMetadata<IAllowAnonymous>() is not null
                && !anonymousRoutes.Contains(route)
            )
            {
                violations.Add($"{route}: allows anonymous access but is not on the declared list");
            }
        }

        return violations;
    }
}
