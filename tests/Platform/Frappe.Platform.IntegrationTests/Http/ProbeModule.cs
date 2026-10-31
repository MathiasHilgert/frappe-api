using Frappe.Kernel;
using Frappe.Kernel.Domain;
using Frappe.Platform.Http.Problems;
using Frappe.Platform.Http.RateLimiting;
using Frappe.Platform.Modularity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;
using NodaTime;

namespace Frappe.Platform.IntegrationTests.Http;

// A fake module whose endpoints fail in every way the platform answers, so the tests see the house shapes.
internal sealed class ProbeModule : IModule
{
    public ModuleMetadata Metadata { get; } =
        new("probe", "probe", "probes", new EventIdRange(1000, 1999), [], [], []);

    public void AddServices(IHostApplicationBuilder builder) { }

    public void MapEndpoints(RouteGroupBuilder group)
    {
        group
            .MapGet("/problems/{kind}", Problem)
            .WithName("GetProblem")
            .WithSummary("Fails with the problem of the given kind.")
            .RequireRateLimiting(RateLimitPolicies.Public)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        group
            .MapPost("/echoes", static (EchoRequest request) => TypedResults.Ok(Echo(request)))
            .WithName("CreateEcho")
            .WithSummary("Answers the request with every wire form.")
            .RequireRateLimiting(RateLimitPolicies.Public)
            .ProducesProblem(StatusCodes.Status400BadRequest);
        group
            .MapGet("/things/{thingId:guid}", static (Guid thingId) => TypedResults.Ok(thingId))
            .WithName("GetThing")
            .WithSummary("Answers a thing by its id.")
            .RequireRateLimiting(RateLimitPolicies.Public);
        group
            .MapPost("/uploads", static (EchoRequest request) => TypedResults.Ok(Echo(request)))
            .WithName("CreateUpload")
            .WithSummary("Accepts a body of at most 64 bytes.")
            .RequireRateLimiting(RateLimitPolicies.Public)
            .WithMetadata(new RequestSizeLimitAttribute(64));
        group
            .MapGet(
                "/failures",
                static IResult () => throw new InvalidOperationException("Host=db;Password=s3cret")
            )
            .WithName("GetFailure")
            .WithSummary("Throws.")
            .RequireRateLimiting(RateLimitPolicies.Public);
        group
            .MapGet(
                "/waits",
                static async (CancellationToken cancellationToken) =>
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                    return TypedResults.NoContent();
                }
            )
            .WithName("GetWait")
            .WithSummary("Waits until the request is cancelled.")
            .RequireRateLimiting(RateLimitPolicies.Public);
    }

    private static Results<NoContent, ProblemHttpResult> Problem(string kind, HttpContext context)
    {
        List<Error> errors = kind switch
        {
            "validation" =>
            [
                .. EntityId.Validate(Guid.Empty).WithPointer("/id").Errors,
                Error.Validation(
                    "probe.name.too_long",
                    "The name must be at most {max} characters.",
                    new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        [Errors.PointerKey] = "/name",
                        ["max"] = 100,
                    }
                ),
            ],
            "not-found" => [Error.NotFound("probe.thing.not_found", "The thing was not found.")],
            "conflict" => [Error.Conflict("probe.thing.closed", "The thing is closed.")],
            "forbidden" => [Error.Forbidden("probe.thing.forbidden", "The thing is not yours.")],
            "malformed" => [PlatformErrors.InvalidCursor],
            "unauthorized" =>
            [
                Error.Unauthorized("probe.session.missing", "No session SECRET-SESSION-ID."),
            ],
            "untranslated" =>
            [
                Error.Validation(
                    "probe.thing.untranslated",
                    "Constraint SECRET-CONSTRAINT failed."
                ),
            ],
            "mixed" =>
            [
                Error.Validation("probe.name.too_long", "The name is too long."),
                Error.Unexpected(
                    "probe.thing.unexpected",
                    "SECRET-INTERNAL",
                    new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        [Errors.PointerKey] = "/SECRET-POINTER",
                        ["table"] = "SECRET-TABLE",
                    }
                ),
            ],
            _ => [Error.Unexpected("probe.thing.unexpected", "SECRET-INTERNAL")],
        };
        return errors.ToProblem(context);
    }

    private static EchoResponse Echo(EchoRequest request) =>
        new(request.Name, new LocalDate(2026, 4, 2), EchoColor.DarkRed, null);
}
