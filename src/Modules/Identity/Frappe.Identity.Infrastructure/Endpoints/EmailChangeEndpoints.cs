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

/// <summary>Changing the sign-in email: request after a step-up, then confirm with the code sent to the new address.</summary>
internal static class EmailChangeEndpoints
{
    private const string Path = "/me/email-changes";

    public static RouteGroupBuilder MapEmailChangeEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost(Path, RequestAsync)
            .RequireAuthorization(IdentityPolicies.OwnerSelf)
            .RequireStepUp()
            .WithName("RequestEmailChange");
        group.MapPost($"{Path}/{{id:guid}}/confirmation", ConfirmAsync)
            .RequireAuthorization(IdentityPolicies.OwnerSelf)
            .WithName("ConfirmEmailChange");
        return group;
    }

    private static async Task<IResult> RequestAsync(
        EmailChangeRequest request, ICurrentPrincipal current, IMessageBus bus, HttpRequest http,
        CancellationToken cancellationToken)
    {
        if (current.Session is not { } caller)
            return TypedResults.Unauthorized();

        var outcome = await bus.InvokeAsync<Throttled<Guid>>(
            new RequestEmailChange(caller.PrincipalId, request.NewEmail), cancellationToken);
        if (outcome.Result is not { } result)
            return TooManyAttempts(outcome.RetryAfter);

        return result.IsSuccess
            ? TypedResults.Created($"{http.PathBase}{http.Path}/{result.Value}", new EmailChangeResponse(result.Value))
            : result.ToProblem();
    }

    private static async Task<IResult> ConfirmAsync(
        Guid id, EmailChangeConfirmationRequest request, ICurrentPrincipal current, IMessageBus bus,
        CancellationToken cancellationToken)
    {
        if (current.Session is not { } caller)
            return TypedResults.Unauthorized();

        var outcome = await bus.InvokeAsync<Throttled<bool>>(
            new ConfirmEmailChange(caller.PrincipalId, id, request.Code), cancellationToken);
        if (outcome.Result is not { } result)
            return TooManyAttempts(outcome.RetryAfter);

        // Each error's Kind picks the status: 409 email taken, 404 no such change, 422 otherwise.
        return result.IsSuccess ? TypedResults.NoContent() : result.ToProblem();
    }

    private static IResult TooManyAttempts(TimeSpan retryAfter) =>
        Result.Failure(IdentityErrors.TooManyAttempts).ToProblem(retryAfter);
}
