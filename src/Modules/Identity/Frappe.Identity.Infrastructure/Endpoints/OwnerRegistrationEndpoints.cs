using Frappe.Identity.Application.Owners;
using Frappe.Platform.Localization;
using Frappe.Platform.Web.Forwarding;
using Frappe.Platform.Web.Problems;
using Frappe.SharedKernel.Validation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Wolverine;

namespace Frappe.Identity.Infrastructure.Endpoints;

/// <summary>Owner sign-up and verification-code resend. Both are public: the caller has no session yet.</summary>
internal static class OwnerRegistrationEndpoints
{
    public static RouteGroupBuilder MapOwnerRegistrationEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/owners", RegisterAsync)
            .AllowAnonymous()
            .WithName("RegisterOwner")
            .WithSummary("Signs up a pending owner and emails them a verification code.")
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
        group.MapPost("/owners/{id:guid}/verification-codes", ResendAsync)
            .AllowAnonymous()
            .WithName("ResendVerificationCode")
            .WithSummary("Emails a pending owner a fresh verification code.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
        return group;
    }

    private static async Task<Results<Created<RegisteredOwnerResponse>, LocalizedProblemResult>> RegisterAsync(
        RegisterOwnerRequest request, HttpContext http, IRequestLocale requestLocale, IMessageBus bus,
        CancellationToken cancellationToken)
    {
        // An anonymous request has no stored preference, so this is Accept-Language looked up, or the default.
        var registered = await bus.InvokeAsync<Result<Guid>>(
            new RegisterOwner(request.Name, request.Email, request.Password, requestLocale.Current, http.ClientIp()),
            cancellationToken);

        return registered.IsSuccess
            ? TypedResults.Created((string?)null, new RegisteredOwnerResponse(registered.Value))
            : registered.ToProblem();
    }

    private static async Task<Results<Accepted, LocalizedProblemResult>> ResendAsync(
        Guid id, IMessageBus bus, CancellationToken cancellationToken)
    {
        var resent = await bus.InvokeAsync<Result>(new ResendVerificationCode(id), cancellationToken);

        // Each error's Kind picks the status: 404 unknown owner, 409 already verified, 429 (Retry-After) slow down.
        return resent.IsSuccess ? TypedResults.Accepted((string?)null) : resent.ToProblem();
    }
}
