using System.Globalization;
using Frappe.Identity.Application.Owners;
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
        IRequestLocale requestLocale,
        IMessageBus bus,
        CancellationToken cancellationToken)
    {
        var ip = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        // An anonymous request has no stored preference, so this is Accept-Language looked up, or the default.
        var registered = await bus.InvokeAsync<Result<Guid>>(
            new RegisterOwner(request.Name, request.Email, request.Password, requestLocale.Current, ip), cancellationToken);

        if (registered.IsSuccess)
            return TypedResults.Created((string?)null, new RegisteredOwnerResponse(registered.Value));
        return registered.Errors.Any(e => e == IdentityErrors.EmailTaken) ? EmailTaken(http) : registered.ToProblem();
    }

    private static async Task<IResult> ResendAsync(Guid id, HttpContext http, IMessageBus bus, CancellationToken cancellationToken)
    {
        var resent = await bus.InvokeAsync<Result>(new ResendVerificationCode(id), cancellationToken);
        if (resent.IsSuccess)
            return TypedResults.Accepted((string?)null);

        var error = resent.Errors[0];
        if (error == ResendVerificationCodeHandler.OwnerNotFound)
            return TypedResults.Problem(title: http.LocalizedMessage(error), statusCode: StatusCodes.Status404NotFound, type: "urn:frappe:problem:not_found");
        if (error == IdentityErrors.AccountNotPending)
            return TypedResults.Problem(title: http.LocalizedMessage(error), statusCode: StatusCodes.Status409Conflict, type: "urn:frappe:problem:account_not_pending");
        return error.Code == IdentityErrors.ResendLimitReached.Code && error.RetryAfter is { } wait
            ? ResendLimitReached(http, wait, error)
            : resent.ToProblem();
    }

    private static ProblemHttpResult EmailTaken(HttpContext http) =>
        TypedResults.Problem(
            title: http.LocalizedMessage(IdentityErrors.EmailTaken),
            statusCode: StatusCodes.Status409Conflict,
            type: "urn:frappe:problem:email_taken",
            extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = "email_taken" });

    private static ProblemHttpResult ResendLimitReached(HttpContext http, TimeSpan retryAfter, Error error)
    {
        var seconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
        http.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
        return TypedResults.Problem(
            title: http.LocalizedMessage(error),
            statusCode: StatusCodes.Status429TooManyRequests,
            type: "urn:frappe:problem:too_many_attempts",
            extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = error.Code });
    }
}
