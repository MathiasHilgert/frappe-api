using Frappe.Identity.Application.PasswordResets;
using Frappe.Identity.Domain;
using Frappe.Platform.Web.Problems;
using Frappe.SharedKernel.Validation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Wolverine;

namespace Frappe.Identity.Infrastructure.Endpoints;

/// <summary>Signed-out password reset by emailed code (S7, S8). Both routes are public by design.</summary>
internal static class PasswordResetEndpoints
{
    public sealed record ResetRequest(string? Email);

    public sealed record ResetAccepted(Guid Id);

    public sealed record ResetConfirmation(string? Code, string? NewPassword);

    public static RouteGroupBuilder MapPasswordResets(this RouteGroupBuilder group)
    {
        var resets = group.MapGroup("/password-resets").AllowAnonymous();
        resets.MapPost("/", RequestAsync).WithName("RequestPasswordReset");
        resets.MapPost("/{id:guid}/confirmation", ConfirmAsync).WithName("ConfirmPasswordReset");
        return group;
    }

    private static async Task<IResult> RequestAsync(ResetRequest body, HttpContext http, IMessageBus bus, CancellationToken cancellationToken)
    {
        var outcome = await bus.InvokeAsync<PasswordResetRequested>(
            new RequestPasswordReset(body.Email, ClientIp(http)), cancellationToken);

        if (outcome.RetryAfter > TimeSpan.Zero)
            return Result.Failure(IdentityErrors.TooManyAttempts).ToProblem(outcome.RetryAfter);
        return outcome.Validation.IsFailure
            ? outcome.Validation.ToProblem()
            : TypedResults.Accepted((string?)null, new ResetAccepted(outcome.ResetId));
    }

    private static async Task<IResult> ConfirmAsync(
        Guid id, ResetConfirmation body, HttpContext http, IMessageBus bus, CancellationToken cancellationToken)
    {
        var outcome = await bus.InvokeAsync<PasswordResetConfirmed>(
            new ConfirmPasswordReset(id, body.Code, body.NewPassword, ClientIp(http)), cancellationToken);

        if (outcome.RetryAfter > TimeSpan.Zero)
            return Result.Failure(IdentityErrors.TooManyAttempts).ToProblem(outcome.RetryAfter);
        return outcome.Outcome.IsFailure ? outcome.Outcome.ToProblem() : TypedResults.NoContent();
    }

    private static string ClientIp(HttpContext http) => http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
