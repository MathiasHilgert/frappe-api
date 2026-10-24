using System.Globalization;
using Frappe.Identity.Application.Account;
using Frappe.Identity.Application.Sessions;
using Frappe.Identity.Domain;
using Frappe.Identity.Domain.Principals;
using Frappe.Platform.Localization;
using Frappe.Platform.Web.Problems;
using Frappe.SharedKernel.Localization;
using Frappe.SharedKernel.Validation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Net.Http.Headers;
using Wolverine;

namespace Frappe.Identity.Infrastructure.Sessions;

/// <summary>Login, logout and the signed-in owner's profile (S7).</summary>
internal static class SessionEndpoints
{
    public static RouteGroupBuilder MapSessionEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/sessions", LogInAsync).AllowAnonymous().WithName("LogIn");
        group.MapDelete("/sessions/current", LogOutAsync).RequireAuthorization(IdentityPolicies.OwnerSelf).WithName("LogOut");
        group.MapGet("/me", GetMeAsync).RequireAuthorization(IdentityPolicies.OwnerSelf).WithName("GetMe");
        group.MapPatch("/me", UpdateMeAsync).RequireAuthorization(IdentityPolicies.OwnerSelf).WithName("UpdateMe");
        return group;
    }

    public sealed record LogInRequest(string Email, string Password);

    /// <summary>A partial update of the profile; only the locale is editable today.</summary>
    public sealed record UpdateMeRequest(string? Locale);

    public sealed record SessionResponse(Guid Id, Guid PrincipalId, string Token, DateTimeOffset ExpiresAt);

    private static async Task<IResult> LogInAsync(
        LogInRequest request, HttpContext http, IMessageBus bus, ISessionCookie cookie, CancellationToken cancellationToken)
    {
        var clientIp = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        // The web console marks its requests with the CSRF header; any other client is an API surface.
        var surface = http.Request.Headers.ContainsKey(SessionAuthenticationHandler.CsrfHeader) ? SessionSurface.Console : SessionSurface.Api;

        var signedIn = await bus.InvokeAsync<Result<SignedIn>>(
            new LogIn(request.Email ?? string.Empty, request.Password ?? string.Empty, clientIp, surface), cancellationToken);
        if (signedIn.IsSuccess)
        {
            var opened = signedIn.Value;
            cookie.Issue(opened.Token, opened.ExpiresAt);
            return TypedResults.Created("/v1/identity/sessions/current",
                new SessionResponse(opened.SessionId, opened.PrincipalId, opened.Token, opened.ExpiresAt));
        }

        return LogInProblem(http, signedIn.Errors[0]);
    }

    /// <summary>A failed login as a problem: 401 for bad credentials, 429 (with Retry-After) when throttled, 403 otherwise.</summary>
    internal static ProblemHttpResult LogInProblem(HttpContext http, Error error)
    {
        switch (error.Kind)
        {
            case ErrorKind.Unauthorized:
                return Problem(http, StatusCodes.Status401Unauthorized, error);
            case ErrorKind.TooManyRequests:
                if (error.RetryAfter is { } wait)
                    http.Response.Headers[HeaderNames.RetryAfter] =
                        Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                return Problem(http, StatusCodes.Status429TooManyRequests, error);
            default:
                return Problem(http, StatusCodes.Status403Forbidden, error);
        }
    }

    private static async Task<IResult> LogOutAsync(
        ICurrentPrincipal current, IMessageBus bus, ISessionCookie cookie, CancellationToken cancellationToken)
    {
        var caller = current.Session!;
        await bus.InvokeAsync<Result>(new LogOut(caller.PrincipalId, caller.SessionId), cancellationToken);
        cookie.Clear();
        return TypedResults.NoContent();
    }

    private static async Task<IResult> GetMeAsync(ICurrentPrincipal current, IMessageBus bus, CancellationToken cancellationToken) =>
        await bus.InvokeAsync<OwnerProfile?>(new GetMe(current.Session!.PrincipalId), cancellationToken) is { } profile
            ? TypedResults.Ok(profile)
            : TypedResults.Unauthorized();

    // Only a locale the platform serves is stored; a regional variant it lacks maps to a sibling (es-CL to es-AR), as
    // negotiation would, while a language it does not speak at all is refused rather than silently stored as English.
    private static async Task<IResult> UpdateMeAsync(
        UpdateMeRequest request, ICurrentPrincipal current, SupportedLocales supported, IMessageBus bus,
        IRequestLocale requestLocale, CancellationToken cancellationToken)
    {
        var parsed = Locale.Create(request.Locale);
        if (parsed.IsFailure)
            return parsed.ToProblem();
        if (supported.Lookup(parsed.Value) is not { } locale)
            return Result.Failure(IdentityErrors.LocaleUnsupported).ToProblem();

        var principalId = current.Session!.PrincipalId;
        var changed = await bus.InvokeAsync<Result>(new ChangeLocale(principalId, locale), cancellationToken);
        if (changed.IsFailure || await bus.InvokeAsync<OwnerProfile?>(new GetMe(principalId), cancellationToken) is not { } profile)
            return TypedResults.Unauthorized();

        requestLocale.AdoptPreferred(locale);
        return TypedResults.Ok(profile);
    }

    // The owner id is only disclosed after the password was proven, so the console can route a pending owner to verify.
    private static ProblemHttpResult Problem(HttpContext http, int status, Error error)
    {
        var extensions = new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = error.Code };
        if (error.Metadata?.GetValueOrDefault(LogInHandler.OwnerIdKey) is Guid id)
            extensions[LogInHandler.OwnerIdKey] = id;

        var message = http.LocalizedMessage(error);
        return TypedResults.Problem(detail: message, statusCode: status, title: message, type: $"urn:frappe:problem:{error.Code}", extensions: extensions);
    }
}
