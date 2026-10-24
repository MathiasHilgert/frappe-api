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

    public static class GoodEndpoints
    {
        public static IResult Handle() => TypedResults.NoContent();
    }
}

namespace Frappe.ArchitectureTests.Fixtures.Edge.Infrastructure.Owners
{
    public static class StrayEndpoints
    {
        public static IResult Handle() => TypedResults.NoContent();
    }
}
