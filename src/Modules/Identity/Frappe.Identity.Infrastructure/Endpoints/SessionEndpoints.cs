using Frappe.Identity.Application.Account;
using Frappe.Identity.Application.Sessions;
using Frappe.Identity.Domain.Principals;
using Frappe.Identity.Infrastructure.Sessions;
using Frappe.Platform.Localization;
using Frappe.Platform.Web.Forwarding;
using Frappe.Platform.Web.Problems;
using Frappe.Platform.Web.Security;
using Frappe.SharedKernel.Localization;
using Frappe.SharedKernel.Validation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Wolverine;

namespace Frappe.Identity.Infrastructure.Endpoints;

/// <summary>Login, logout and the signed-in owner's profile (S7).</summary>
internal static class SessionEndpoints
{
    public static RouteGroupBuilder MapSessionEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/sessions", LogInAsync)
            .AllowAnonymousAuth()
            .WithName("LogIn")
            .WithSummary("Signs an owner in with email and password, issuing the session cookie and token.")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
        group.MapDelete("/sessions/current", LogOutAsync)
            .RequireAuthorization(IdentityPolicies.OwnerSelf)
            .WithName("LogOut")
            .WithSummary("Revokes the caller's session and clears the cookie.");
        group.MapGet("/me", GetMeAsync)
            .RequireAuthorization(IdentityPolicies.OwnerSelf)
            .WithName("GetMe")
            .WithSummary("The signed-in owner's profile.");
        group.MapPatch("/me", UpdateMeAsync)
            .RequireAuthorization(IdentityPolicies.OwnerSelf)
            .WithName("UpdateMe")
            .WithSummary("Updates the signed-in owner's profile; only the locale today.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        return group;
    }

    private static async Task<Results<Created<OpenedSessionResponse>, LocalizedProblemResult>> LogInAsync(
        LogInRequest request, HttpContext http, IMessageBus bus, ISessionCookie cookie, CancellationToken cancellationToken)
    {
        // The web console marks its requests with the CSRF header; any other client is an API surface.
        var surface = http.Request.Headers.ContainsKey(SessionAuthenticationHandler.CsrfHeader) ? SessionSurface.Console : SessionSurface.Api;

        var signedIn = await bus.InvokeAsync<Result<SignedIn>>(
            new LogIn(request.Email ?? string.Empty, request.Password ?? string.Empty, http.ClientIp(), surface),
            cancellationToken);
        // 401 bad credentials, 429 (Retry-After) throttled, 403 pending/suspended with the owner id disclosed.
        if (!signedIn.TryGetValue(out var opened))
            return signedIn.ToProblem();

        cookie.Issue(opened.Token, opened.ExpiresAt);
        return TypedResults.Created("/v1/identity/sessions/current",
            new OpenedSessionResponse(opened.SessionId, opened.PrincipalId, opened.Token, opened.ExpiresAt));
    }

    private static async Task<Results<NoContent, LocalizedProblemResult>> LogOutAsync(
        ICurrentPrincipal current, IMessageBus bus, ISessionCookie cookie, CancellationToken cancellationToken)
    {
        var caller = current.RequiredSession();
        var loggedOut = await bus.InvokeAsync<Result>(new LogOut(caller.PrincipalId, caller.SessionId), cancellationToken);
        if (loggedOut.IsFailure)
            return loggedOut.ToProblem();

        cookie.Clear();
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<OwnerProfile>, UnauthorizedHttpResult>> GetMeAsync(
        ICurrentPrincipal current, IMessageBus bus, CancellationToken cancellationToken) =>
        await bus.InvokeAsync<OwnerProfile?>(new GetMe(current.RequiredSession().PrincipalId), cancellationToken) is { } profile
            ? TypedResults.Ok(profile)
            : TypedResults.Unauthorized();

    private static async Task<Results<Ok<OwnerProfile>, UnauthorizedHttpResult, LocalizedProblemResult>> UpdateMeAsync(
        UpdateMeRequest request, ICurrentPrincipal current, IMessageBus bus, IRequestLocale requestLocale,
        CancellationToken cancellationToken)
    {
        var principalId = current.RequiredSession().PrincipalId;
        var changed = await bus.InvokeAsync<Result<Locale>>(new ChangeLocale(principalId, request.Locale), cancellationToken);
        if (!changed.TryGetValue(out var locale))
            return changed.ToProblem();
        if (await bus.InvokeAsync<OwnerProfile?>(new GetMe(principalId), cancellationToken) is not { } profile)
            return TypedResults.Unauthorized();

        requestLocale.AdoptPreferred(locale);
        return TypedResults.Ok(profile);
    }
}
