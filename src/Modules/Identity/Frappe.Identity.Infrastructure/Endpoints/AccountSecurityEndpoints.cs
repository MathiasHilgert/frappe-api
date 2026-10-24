using Frappe.Identity.Application.Account;
using Frappe.Identity.Application.Sessions;
using Frappe.Identity.Domain;
using Frappe.Identity.Infrastructure.Sessions;
using Frappe.Platform.Web.Problems;
using Frappe.SharedKernel.Validation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Wolverine;

namespace Frappe.Identity.Infrastructure.Endpoints;

/// <summary>Re-proving the password inside a session: step-up and change password.</summary>
internal static class AccountSecurityEndpoints
{
    public static RouteGroupBuilder MapAccountSecurityEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/sessions/current/step-ups", CompleteStepUpAsync)
            .RequireAuthorization(IdentityPolicies.OwnerSelf)
            .WithName("CompleteStepUp");
        group.MapPut("/me/password", ChangePasswordAsync)
            .RequireAuthorization(IdentityPolicies.OwnerSelf)
            .WithName("ChangePassword");
        return group;
    }

    private static async Task<IResult> CompleteStepUpAsync(
        StepUpRequest request, ICurrentPrincipal current, IMessageBus bus,
        CancellationToken cancellationToken)
    {
        if (current.Session is not { } caller)
            return TypedResults.Unauthorized();

        var outcome = await bus.InvokeAsync<Throttled<DateTimeOffset>>(
            new CompleteStepUp(caller.PrincipalId, caller.SessionId, request.Password), cancellationToken);
        if (outcome.Result is not { } result)
            return Result.Failure(IdentityErrors.TooManyAttempts).ToProblem(outcome.RetryAfter);

        return result.IsSuccess
            ? TypedResults.Created((string?)null, new StepUpResponse(result.Value))
            : result.ToProblem();
    }

    private static async Task<IResult> ChangePasswordAsync(
        ChangePasswordRequest request, ICurrentPrincipal current, IMessageBus bus,
        CancellationToken cancellationToken)
    {
        if (current.Session is not { } caller)
            return TypedResults.Unauthorized();

        var outcome = await bus.InvokeAsync<Throttled<bool>>(
            new ChangePassword(caller.PrincipalId, caller.SessionId, request.CurrentPassword, request.NewPassword),
            cancellationToken);
        if (outcome.Result is not { } result)
            return Result.Failure(IdentityErrors.TooManyAttempts).ToProblem(outcome.RetryAfter);

        return result.IsSuccess ? TypedResults.NoContent() : result.ToProblem();
    }
}
