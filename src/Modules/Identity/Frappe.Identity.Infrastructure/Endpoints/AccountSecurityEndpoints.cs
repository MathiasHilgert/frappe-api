using Frappe.Identity.Application.Account;
using Frappe.Identity.Application.Sessions;
using Frappe.Identity.Infrastructure.Sessions;
using Frappe.Platform.Web.Problems;
using Frappe.SharedKernel.Validation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Wolverine;

namespace Frappe.Identity.Infrastructure.Endpoints;

/// <summary>Re-proving the password inside a session: step-up and change password.</summary>
internal static class AccountSecurityEndpoints
{
    public static RouteGroupBuilder MapAccountSecurityEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/sessions/current/step-ups", CompleteStepUpAsync)
            .RequireOwner()
            .WithName("CompleteStepUp")
            .WithSummary("Re-proves the password, unlocking sensitive actions on this session for five minutes.")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
        group.MapPut("/me/password", ChangePasswordAsync)
            .RequireOwner()
            .WithName("ChangePassword")
            .WithSummary("Changes the password, signing every other session out.")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
        return group;
    }

    private static async Task<Results<Created<StepUpResponse>, LocalizedProblemResult>> CompleteStepUpAsync(
        StepUpRequest request, ICurrentPrincipal current, IMessageBus bus, CancellationToken cancellationToken)
    {
        var caller = current.RequiredSession();
        var result = await bus.InvokeAsync<Result<DateTimeOffset>>(
            new CompleteStepUp(caller.PrincipalId, caller.SessionId, request.Password), cancellationToken);

        return result.TryGetValue(out var expiresAt)
            ? TypedResults.Created((string?)null, new StepUpResponse(expiresAt))
            : result.ToProblem();
    }

    private static async Task<Results<NoContent, LocalizedProblemResult>> ChangePasswordAsync(
        ChangePasswordRequest request, ICurrentPrincipal current, IMessageBus bus, CancellationToken cancellationToken)
    {
        var caller = current.RequiredSession();
        var result = await bus.InvokeAsync<Result>(
            new ChangePassword(caller.PrincipalId, caller.SessionId, request.CurrentPassword, request.NewPassword),
            cancellationToken);

        return result.IsSuccess ? TypedResults.NoContent() : result.ToProblem();
    }
}
