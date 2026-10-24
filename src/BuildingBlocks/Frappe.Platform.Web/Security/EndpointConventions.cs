using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Frappe.Platform.Web.Security;

/// <summary>
/// The two ways an endpoint is reached, each declaring the problems the platform itself can answer with, so OpenAPI
/// documents them on every endpoint without each one repeating them.
/// </summary>
public static class EndpointConventions
{
    /// <summary>
    /// An endpoint reachable without a session (sign-in, sign-up, password reset): it opts out of deny-by-default, is
    /// rate limited per IP by the in-process backstop, and documents the 429 that can answer.
    /// </summary>
    public static TBuilder AllowAnonymousAuth<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AllowAnonymous()
            .RequireRateLimiting(RateLimitingRegistration.AnonymousAuthPolicy)
            .WithMetadata(Problem(StatusCodes.Status429TooManyRequests));

    /// <summary>An endpoint that needs <paramref name="policy"/>: it documents the 401 and 403 authorization answers with.</summary>
    public static TBuilder RequirePolicy<TBuilder>(this TBuilder builder, string policy)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(policy)
            .WithMetadata(Problem(StatusCodes.Status401Unauthorized), Problem(StatusCodes.Status403Forbidden));

    private static ProducesResponseTypeMetadata Problem(int status) =>
        new(status, typeof(ProblemDetails), ["application/problem+json"]);
}
