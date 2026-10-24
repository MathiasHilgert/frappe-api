// Deliberately broken shapes for EdgeRulesFailureTests; nothing outside those tests uses them.
#pragma warning disable CA1822, S1186, S2325, S3903

using Frappe.SharedKernel.Validation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Frappe.ArchitectureTests.Fixtures.Edge.Infrastructure
{
    public static class StrayErrorHandler
    {
        public static readonly Error Missing = new("fixture.missing", "Missing.", Kind: ErrorKind.NotFound);
    }

    public static class FixtureErrors
    {
        public static readonly Error Allowed = new("fixture.allowed", "Allowed.");
    }
}

namespace Frappe.ArchitectureTests.Fixtures.Edge.Infrastructure.Endpoints
{
    public static class SingularEndpoint
    {
        public static IResult Handle() => TypedResults.NoContent();
    }

    public static class TypedProblemEndpoints
    {
        public static IResult Handle() => TypedResults.Problem(title: "hand-built", statusCode: 409);
    }

    public static class ProblemDetailsEndpoints
    {
        public static IResult Handle() => TypedResults.Problem(new ProblemDetails { Status = 404 });
    }

    public static class ResultsProblemEndpoints
    {
        public static IResult Handle() => Results.Problem("hand-built");
    }

    public static class BareStatusEndpoints
    {
        public static IResult Handle() => TypedResults.Unauthorized();
    }

    public static class RawCallerEndpoints
    {
        public static string Handle(HttpContext http) =>
            $"{http.User.Identity?.Name}@{http.Connection.RemoteIpAddress}";
    }

    public static class DtoPayloadEndpoints
    {
        public static Microsoft.AspNetCore.Http.HttpResults.Ok<FixtureProfile> Handle() => TypedResults.Ok(new FixtureProfile("x"));
    }

    public static class GoodEndpoints
    {
        public static IResult Handle() => TypedResults.NoContent();

        public static Microsoft.AspNetCore.Http.HttpResults.Ok<FixtureResponse> Read() => TypedResults.Ok(new FixtureResponse("x"));
    }

    public sealed record FixtureResponse(string Value);

    public sealed record FixtureProfile(string Value);
}

namespace Frappe.ArchitectureTests.Fixtures.Edge.Infrastructure.Owners
{
    public static class StrayEndpoints
    {
        public static IResult Handle() => TypedResults.NoContent();
    }
}
