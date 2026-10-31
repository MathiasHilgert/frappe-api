using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;

namespace Frappe.TestUtilities;

/// <summary>
/// The route standard every mapped endpoint meets: <c>/v1</c> resource anatomy without verbs, exactly one rate-limit
/// policy, a stable name, a place in the OpenAPI document, and anonymous access only when declared. Health, the
/// OpenAPI document and Scalar are the declared internal endpoints and are exempt.
/// </summary>
public static partial class RouteStandard
{
    private static readonly string[] _internalPrefixes = ["/health", "/openapi/", "/scalar"];

    private static readonly HashSet<string> _verbs = new(StringComparer.Ordinal)
    {
        "get",
        "create",
        "update",
        "delete",
        "remove",
        "add",
        "set",
        "close",
        "fire",
        "cancel",
        "list",
        "search",
        "find",
        "fetch",
        "execute",
        "run",
        "submit",
    };

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

            violations.AddRange(
                AnatomyViolations(route)
                    .Concat(MetadataViolations(endpoint.Metadata, route, anonymousRoutes))
                    .Select(violation => $"{route}: {violation}")
            );
        }

        return violations;
    }

    private static IEnumerable<string> MetadataViolations(
        EndpointMetadataCollection metadata,
        string route,
        IReadOnlySet<string> anonymousRoutes
    )
    {
        if (
            metadata.GetOrderedMetadata<EnableRateLimitingAttribute>().Count != 1
            || metadata.GetMetadata<DisableRateLimitingAttribute>() is not null
        )
        {
            yield return "declares no single rate-limit policy";
        }

        if (metadata.GetMetadata<IEndpointNameMetadata>() is null)
        {
            yield return "has no WithName";
        }

        if (
            metadata.GetMetadata<IExcludeFromDescriptionMetadata>() is
            { ExcludeFromDescription: true }
        )
        {
            yield return "is excluded from the OpenAPI document";
        }

        if (metadata.GetMetadata<IAllowAnonymous>() is not null && !anonymousRoutes.Contains(route))
        {
            yield return "allows anonymous access but is not on the declared list";
        }

        if (
            metadata
                .GetMetadata<HttpMethodMetadata>()
                ?.HttpMethods.Contains(HttpMethods.Trace, StringComparer.OrdinalIgnoreCase) == true
        )
        {
            yield return "maps TRACE";
        }
    }

    private static IEnumerable<string> AnatomyViolations(string route)
    {
        string[] segments = route.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments is not ["v1", _, _, ..] || segments.Length > 6)
        {
            yield return "does not follow /v1/{context}/{resource}/{id}/{contained}";
        }

        foreach (string segment in segments.Skip(1))
        {
            if (segment.StartsWith('{'))
            {
                if (!Parameter().IsMatch(segment))
                {
                    yield return $"parameter {segment} is not a camelCase name";
                }
            }
            else if (!KebabNoun().IsMatch(segment))
            {
                yield return $"segment {segment} is not lowercase kebab-case";
            }
            else if (segment.Split('-').Any(_verbs.Contains))
            {
                yield return $"segment {segment} contains a verb";
            }
        }
    }

    [GeneratedRegex(
        "^[a-z][a-z0-9]*(-[a-z0-9]+)*$",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        1000
    )]
    private static partial Regex KebabNoun();

    [GeneratedRegex(
        "^\\{[a-z][A-Za-z0-9]*(:[a-z]+)?\\}$",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        1000
    )]
    private static partial Regex Parameter();
}
