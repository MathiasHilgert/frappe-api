using System.Globalization;
using Frappe.Identity.Application.Owners;
using Frappe.Identity.Application.Security;
using Frappe.Identity.Domain;
using Frappe.Platform.Localization;
using Frappe.Platform.Web.Problems;
using Frappe.SharedKernel.Validation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Wolverine;

namespace Frappe.Identity.Infrastructure.Owners;

/// <summary>Owner sign-up and verification-code resend. Both are public: the caller has no session yet.</summary>
internal static class OwnerRegistrationEndpoints
{
    public sealed record RegisterOwnerRequest(string? Name, string? Email, string? Password);

    public sealed record RegisteredOwnerResponse(Guid Id);

    public static void MapOwnerRegistration(this RouteGroupBuilder group)
    {
        group.MapPost("/owners", RegisterAsync).AllowAnonymous().WithName("RegisterOwner");
        group.MapPost("/owners/{id:guid}/verification-codes", ResendAsync).AllowAnonymous().WithName("ResendVerificationCode");
    }

    private static async Task<IResult> RegisterAsync(
        RegisterOwnerRequest request,
        HttpContext http,
        IAttemptLimiter limiter,
        IRequestLocale requestLocale,
        IMessageBus bus,
        CancellationToken cancellationToken)
    {
        // Every attempt counts, successful or not: the limit is what keeps "email taken" from being a free oracle.
        var ip = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var perIp = await limiter.RecordAsync(AttemptPolicies.RegisterPerIp, ip, cancellationToken);
        var perEmail = await limiter.RecordAsync(AttemptPolicies.RegisterPerEmail, request.Email?.Trim() ?? string.Empty, cancellationToken);
        if (!perIp.IsAllowed || !perEmail.IsAllowed)
            return TooManyAttempts(http, perIp.RetryAfter > perEmail.RetryAfter ? perIp.RetryAfter : perEmail.RetryAfter);

        // An anonymous request has no stored preference, so this is Accept-Language looked up, or the default.
        var registered = await bus.InvokeAsync<Result<Guid>>(
            new RegisterOwner(request.Name, request.Email, request.Password, requestLocale.Current), cancellationToken);

        if (registered.IsSuccess)
            return TypedResults.Created((string?)null, new RegisteredOwnerResponse(registered.Value));
        return registered.Errors.Any(e => e == IdentityErrors.EmailTaken) ? EmailTaken() : registered.ToProblem();
    }

    private static async Task<IResult> ResendAsync(Guid id, HttpContext http, IMessageBus bus, CancellationToken cancellationToken)
    {
        var resent = await bus.InvokeAsync<ResendVerificationCodeResult>(new ResendVerificationCode(id), cancellationToken);
        if (resent.Result.IsSuccess)
            return TypedResults.Accepted((string?)null);

        var error = resent.Result.Errors[0];
        if (error == ResendVerificationCodeHandler.OwnerNotFound)
            return TypedResults.Problem(title: error.Message, statusCode: StatusCodes.Status404NotFound, type: "urn:frappe:problem:not_found");
        if (error == IdentityErrors.AccountNotPending)
            return TypedResults.Problem(title: error.Message, statusCode: StatusCodes.Status409Conflict, type: "urn:frappe:problem:account_not_pending");
        return error == IdentityErrors.ResendLimitReached ? TooManyAttempts(http, resent.RetryAfter, error) : resent.Result.ToProblem();
    }

    private static ProblemHttpResult EmailTaken() =>
        TypedResults.Problem(
            title: IdentityErrors.EmailTaken.Message,
            statusCode: StatusCodes.Status409Conflict,
            type: "urn:frappe:problem:email_taken",
            extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = "email_taken" });

    private static ProblemHttpResult TooManyAttempts(HttpContext http, TimeSpan retryAfter, Error? error = null)
    {
        var seconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
        http.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
        error ??= IdentityErrors.TooManyAttempts;
        return TypedResults.Problem(
            title: error.Message,
            statusCode: StatusCodes.Status429TooManyRequests,
            type: "urn:frappe:problem:too_many_attempts",
            extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = error.Code });
    }
}
