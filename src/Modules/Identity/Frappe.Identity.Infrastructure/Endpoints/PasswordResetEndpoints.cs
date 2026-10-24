using Frappe.Identity.Application.PasswordResets;
using Frappe.Platform.Web.Forwarding;
using Frappe.Platform.Web.Problems;
using Frappe.SharedKernel.Validation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Wolverine;

namespace Frappe.Identity.Infrastructure.Endpoints;

/// <summary>Signed-out password reset by emailed code (S7, S8). Both routes are public by design.</summary>
internal static class PasswordResetEndpoints
{
    public static RouteGroupBuilder MapPasswordResetEndpoints(this RouteGroupBuilder group)
    {
        var resets = group.MapGroup("/password-resets").AllowAnonymous();
        resets.MapPost("/", RequestAsync)
            .WithName("RequestPasswordReset")
            .WithSummary("Emails a reset code when the address belongs to an owner; answers the same either way.")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
        resets.MapPost("/{id:guid}/confirmation", ConfirmAsync)
            .WithName("ConfirmPasswordReset")
            .WithSummary("Sets a new password with the emailed code, signing every session out.")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
        return group;
    }

    private static async Task<Results<Accepted<PasswordResetResponse>, LocalizedProblemResult>> RequestAsync(
        PasswordResetRequest body, HttpContext http, IMessageBus bus, CancellationToken cancellationToken)
    {
        var requested = await bus.InvokeAsync<Result<Guid>>(
            new RequestPasswordReset(body.Email, http.ClientIp()), cancellationToken);

        return requested.TryGetValue(out var resetId)
            ? TypedResults.Accepted((string?)null, new PasswordResetResponse(resetId))
            : requested.ToProblem();
    }

    private static async Task<Results<NoContent, LocalizedProblemResult>> ConfirmAsync(
        Guid id, PasswordResetConfirmationRequest body, HttpContext http, IMessageBus bus, CancellationToken cancellationToken)
    {
        var confirmed = await bus.InvokeAsync<Result>(
            new ConfirmPasswordReset(id, body.Code, body.NewPassword, http.ClientIp()), cancellationToken);

        return confirmed.IsSuccess ? TypedResults.NoContent() : confirmed.ToProblem();
    }
}
