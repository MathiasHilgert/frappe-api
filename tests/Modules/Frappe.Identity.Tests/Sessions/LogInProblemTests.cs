using Frappe.Identity.Application.Sessions;
using Frappe.Identity.Domain;
using Frappe.Identity.Infrastructure.Sessions;
using Frappe.SharedKernel.Validation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Frappe.Identity.Tests.Sessions;

/// <summary>How a failed login maps to a problem: by the error's kind, never by which optional details it carries.</summary>
public sealed class LogInProblemTests
{
    [Fact]
    public void InvalidCredentials_Is401_WithItsCode()
    {
        var (problem, http) = Map(IdentityErrors.InvalidCredentials);

        problem.StatusCode.ShouldBe(StatusCodes.Status401Unauthorized);
        problem.ProblemDetails.Extensions["code"].ShouldBe(IdentityErrors.InvalidCredentials.Code);
        problem.ProblemDetails.Extensions.ShouldNotContainKey(LogInHandler.OwnerIdKey);
        http.Response.Headers.ContainsKey("Retry-After").ShouldBeFalse();
    }

    [Fact]
    public void AccountPending_Is403_DisclosingTheOwnerId()
    {
        var ownerId = Guid.CreateVersion7();

        var (problem, _) = Map(IdentityErrors.AccountPending.WithMetadata(LogInHandler.OwnerIdKey, ownerId));

        problem.StatusCode.ShouldBe(StatusCodes.Status403Forbidden);
        problem.ProblemDetails.Type.ShouldBe($"urn:frappe:problem:{IdentityErrors.AccountPending.Code}");
        problem.ProblemDetails.Extensions[LogInHandler.OwnerIdKey].ShouldBe(ownerId);
    }

    [Fact]
    public void TooManyAttempts_Is429_WithRetryAfterInWholeSecondsRoundedUp()
    {
        var (problem, http) = Map(IdentityErrors.TooManyAttempts.WithRetryAfter(TimeSpan.FromSeconds(29.2)));

        problem.StatusCode.ShouldBe(StatusCodes.Status429TooManyRequests);
        http.Response.Headers["Retry-After"].ToString().ShouldBe("30");
    }

    [Fact]
    public void TooManyRequests_WithoutAWait_IsStill429()
    {
        var (problem, http) = Map(IdentityErrors.TooManyAttempts);

        problem.StatusCode.ShouldBe(StatusCodes.Status429TooManyRequests);
        http.Response.Headers.ContainsKey("Retry-After").ShouldBeFalse();
    }

    [Fact]
    public void AWaitOnAForbiddenError_DoesNotTurnItInto429()
    {
        var (problem, http) = Map(IdentityErrors.AccountSuspended.WithRetryAfter(TimeSpan.FromSeconds(5)));

        problem.StatusCode.ShouldBe(StatusCodes.Status403Forbidden);
        http.Response.Headers.ContainsKey("Retry-After").ShouldBeFalse();
    }

    private static (Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult Problem, HttpContext Http) Map(Error error)
    {
        var http = new DefaultHttpContext { RequestServices = new ServiceCollection().BuildServiceProvider() };
        return (SessionEndpoints.LogInProblem(http, error), http);
    }
}
