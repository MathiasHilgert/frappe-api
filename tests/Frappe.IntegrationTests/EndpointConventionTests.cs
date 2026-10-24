using Frappe.Platform.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Frappe.IntegrationTests;

/// <summary>
/// Conventions every module endpoint keeps, read from the running host's route table so a new endpoint is covered the
/// moment it is mapped.
/// </summary>
public sealed class EndpointConventionTests(ApiFixture fixture)
{
    // Module groups are tagged with the module name (MapFrappe); platform endpoints such as /v1/stream are not.
    private IReadOnlyList<RouteEndpoint> ModuleEndpoints =>
    [
        .. fixture.Host.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/v1/", StringComparison.Ordinal) == true
                        && e.Metadata.GetMetadata<ITagsMetadata>() is not null),
    ];

    [Fact]
    public void Endpoints_LiveUnderTheirModulesNameAsTheRouteSegment()
    {
        var endpoints = ModuleEndpoints;

        endpoints.ShouldNotBeEmpty();
        endpoints.ShouldAllBe(e => e.RoutePattern.RawText!.Split('/', StringSplitOptions.RemoveEmptyEntries)[1]
            == e.Metadata.GetMetadata<ITagsMetadata>()!.Tags.Single());
        endpoints.ShouldAllBe(e => e.Metadata.GetMetadata<ITagsMetadata>()!.Tags.Single().All(c => !char.IsUpper(c)));
    }

    [Fact]
    public void Endpoints_HaveANameASummaryAndTheirProblemsDocumented()
    {
        foreach (var endpoint in ModuleEndpoints)
        {
            var name = endpoint.DisplayName;
            endpoint.Metadata.GetMetadata<IEndpointNameMetadata>().ShouldNotBeNull(name);
            endpoint.Metadata.GetMetadata<IEndpointSummaryMetadata>()?.Summary.ShouldNotBeNullOrWhiteSpace(name);
            endpoint.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>()
                .ShouldContain(p => p.StatusCode >= 400 && p.ContentTypes.Contains("application/problem+json"), name);
        }
    }

    [Fact]
    public void Endpoints_AreEitherAnonymousAuthWithTheRateLimit_OrAuthorizedWithTheir401And403()
    {
        foreach (var endpoint in ModuleEndpoints)
        {
            var name = endpoint.DisplayName;
            var problems = endpoint.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>().Select(p => p.StatusCode).ToList();
            if (endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            {
                endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName
                    .ShouldBe(RateLimitingRegistration.AnonymousAuthPolicy, name);
                problems.ShouldContain(StatusCodes.Status429TooManyRequests, name);
            }
            else
            {
                endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().ShouldNotBeEmpty(name);
                problems.ShouldContain(StatusCodes.Status401Unauthorized, name);
                problems.ShouldContain(StatusCodes.Status403Forbidden, name);
            }
        }
    }

    /// <summary>
    /// The step-up policy: a fresh step-up guards what changes how the owner signs in or where account mail goes. The
    /// password change re-verifies the current password inline instead; revoking sessions and signing out stay
    /// one-tap, because an owner who suspects a stolen session must be able to cut it off at once.
    /// </summary>
    [Fact]
    public void StepUp_GuardsExactlyTheEndpointsThatChangeHowTheOwnerSignsIn()
    {
        var guarded = ModuleEndpoints
            .Where(e => e.Metadata.GetMetadata<Frappe.Identity.Infrastructure.Endpoints.StepUpRequiredMetadata>() is not null)
            .Select(e => e.Metadata.GetMetadata<IEndpointNameMetadata>()!.EndpointName);

        guarded.ShouldBe(["RequestEmailChange"], ignoreOrder: true);
    }
}
