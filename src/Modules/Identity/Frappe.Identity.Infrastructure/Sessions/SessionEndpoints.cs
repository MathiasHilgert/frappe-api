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
        LogInRequest request, HttpContext http, LogIn logIn, ISessionCookie cookie, CancellationToken cancellationToken)
    {
        var clientIp = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        // The web console marks its requests with the CSRF header; any other client is an API surface.
        var surface = http.Request.Headers.ContainsKey(SessionAuthenticationHandler.CsrfHeader) ? SessionSurface.Console : SessionSurface.Api;

        var outcome = await logIn.ExecuteAsync(request.Email ?? string.Empty, request.Password ?? string.Empty, clientIp, surface, cancellationToken);
        switch (outcome)
        {
            case LogInOutcome.Opened opened:
                cookie.Issue(opened.Token, opened.ExpiresAt);
                return TypedResults.Created("/v1/identity/sessions/current",
                    new SessionResponse(opened.SessionId, opened.PrincipalId, opened.Token, opened.ExpiresAt));
            case LogInOutcome.Throttled throttled:
                http.Response.Headers[HeaderNames.RetryAfter] =
                    Math.Max(1, (int)Math.Ceiling(throttled.RetryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                return Problem(StatusCodes.Status429TooManyRequests, IdentityErrors.TooManyAttempts);
            case LogInOutcome.Refused refused:
                return Problem(StatusCodes.Status403Forbidden, refused.Error, refused.PrincipalId);
            default:
                return Problem(StatusCodes.Status401Unauthorized, IdentityErrors.InvalidCredentials);
        }
    }

    private static async Task<IResult> LogOutAsync(
        ICurrentPrincipal current, LogOut logOut, ISessionCookie cookie, CancellationToken cancellationToken)
    {
        await logOut.ExecuteAsync(current.Session!, cancellationToken);
        cookie.Clear();
        return TypedResults.NoContent();
    }

    private static async Task<IResult> GetMeAsync(ICurrentPrincipal current, GetMe getMe, CancellationToken cancellationToken) =>
        await getMe.ExecuteAsync(current.Session!, cancellationToken) is { } profile
            ? TypedResults.Ok(profile)
            : TypedResults.Unauthorized();

    // Only a locale the platform serves is stored; a regional variant it lacks maps to a sibling (es-CL to es-AR), as
    // negotiation would, while a language it does not speak at all is refused rather than silently stored as English.
    private static async Task<IResult> UpdateMeAsync(
        UpdateMeRequest request, ICurrentPrincipal current, SupportedLocales supported, ChangeLocale changeLocale,
        CancellationToken cancellationToken)
    {
        var parsed = Locale.Create(request.Locale);
        if (parsed.IsFailure)
            return parsed.ToProblem();
        if (supported.Lookup(parsed.Value) is not { } locale)
            return Result.Failure(IdentityErrors.LocaleUnsupported).ToProblem();

        return await changeLocale.HandleAsync(current.Session!, locale, cancellationToken) is { } profile
            ? TypedResults.Ok(profile)
            : TypedResults.Unauthorized();
    }

    // The owner id is only disclosed after the password was proven, so the console can route a pending owner to verify.
    private static ProblemHttpResult Problem(int status, Error error, Guid? ownerId = null)
    {
        var extensions = new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = error.Code };
        if (ownerId is { } id)
            extensions["ownerId"] = id;

        return TypedResults.Problem(detail: error.Message, statusCode: status, type: $"urn:frappe:problem:{error.Code}", extensions: extensions);
    }
}
