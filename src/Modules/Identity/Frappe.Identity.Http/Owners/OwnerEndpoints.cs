using Frappe.Http;
using Frappe.Identity.Application.Owners.RegisterOwner;
using Frappe.Identity.Application.Owners.ResendOwnerVerification;
using Frappe.Identity.Application.Owners.VerifyOwnerEmail;
using Frappe.Identity.Application.Sessions;
using Frappe.Identity.Http.Sessions;
using Frappe.Results;
using Frappe.UseCases;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Net.Http.Headers;

namespace Frappe.Identity.Http.Owners;

/// <summary>
/// Becoming an owner (S3, S6): <c>POST /owners</c> registers, <c>POST /owners/{ownerId}/verification</c> proves the address with the mailed code
/// and opens a session, and <c>POST /owners/{ownerId}/verification-codes</c> asks for a new code. All are anonymous and never cached; none has
/// anything to follow, so none answers a <c>Location</c>. The id is read as text and judged by the use case: a malformed id is an unknown one.
/// </summary>
internal static class OwnerEndpoints
{
    extension(IEndpointRouteBuilder routes)
    {
        public void MapOwnerEndpoints()
        {
            routes.MapPost("/owners", RegisterAsync)
                .WithName("RegisterOwner").WithTags(IdentityHttp.Tag).NoStore().WithoutLocation().AcceptsIdempotencyKey()
                .RequireRateLimiting(IdentityHttp.RegistrationRateLimitPolicy)
                .WithSummary("Register an owner")
                .WithDescription(
                    "Registers an owner with a name, an email address and a password, and mails a six-digit code to the address. The language "
                    + "of the mail is the one the request negotiates from Accept-Language (an unknown one is answered in English, never "
                    + "refused). It answers 201 with an id to verify the address with. The answer is the same for an address that is new, "
                    + "one a pending owner holds (the new registration takes it over, and the old code stops working) and one a verified "
                    + "owner holds (nothing is written and that owner is told by mail), so it never reveals whether an address is registered. "
                    + "The same Idempotency-Key registers once. 400 identity.email.invalid or identity.name.invalid (a name is at most 60 "
                    + "characters, given plus family, and holds no http, www. or ://), 422 for a password that is too short, too long or "
                    + "breached.")
                .ProducesProblem(StatusCodes.Status400BadRequest)
                .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

            routes.MapPost("/owners/{ownerId}/verification", VerifyAsync)
                .WithName("VerifyOwnerEmail").WithTags(IdentityHttp.Tag).NoStore().WithoutLocation()
                .WithSummary("Verify an owner's email")
                .WithDescription(
                    "Proves the address with the code that was mailed: the owner becomes active and a session opens, so the owner needs no "
                    + "log-in. The session is in the body (send its token as Authorization: Bearer) and, for the web console, in the "
                    + "session cookie. 422 identity.code.invalid for a code that is wrong, expired, used or replaced and for an id that is "
                    + "unknown or malformed (one answer for all of them); a code takes five guesses. 503 identity.attempts.unavailable when "
                    + "guesses cannot be counted.")
                .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
                .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

            routes.MapPost("/owners/{ownerId}/verification-codes", ResendAsync)
                .WithName("ResendOwnerVerification").WithTags(IdentityHttp.Tag).NoStore().WithoutLocation()
                .WithSummary("Resend an owner's verification code")
                .WithDescription(
                    "Mails a new verification code to a pending owner, which replaces the one before it. It answers 202 always, for an id that "
                    + "is unknown, malformed, verified or over its budget of five codes an hour, so it reveals nothing about which owners exist.");
        }
    }

    private static async Task<Results<Created<RegisteredOwnerResponse>, ErrorsProblemHttpResult>> RegisterAsync(
        [FromBody] RegisterOwnerRequest? body, HttpContext httpContext, ICommandHandler<RegisterOwner, RegisteredOwner> handler,
        CancellationToken cancellationToken)
    {
        var command = new RegisterOwner(
            body?.Name?.Given, body?.Name?.Family, body?.Email, body?.Password, LanguageNegotiation.Negotiate(httpContext.Request).Value);
        return (await handler.HandleAsync(command, cancellationToken)).ToTypedHttpResult(registered =>
            TypedResults.Created((string?)null, new RegisteredOwnerResponse(registered.Id)));
    }

    private static async Task<Results<Created<OpenedSessionResponse>, ErrorsProblemHttpResult>> VerifyAsync(
        string ownerId, [FromBody] VerifyOwnerRequest? body, HttpContext httpContext, ICommandHandler<VerifyOwnerEmail, OpenedSession> handler,
        CancellationToken cancellationToken)
    {
        var command = new VerifyOwnerEmail(
            ownerId, body?.Code, httpContext.Request.Headers[HeaderNames.UserAgent].ToString(), httpContext.Connection.RemoteIpAddress);
        return (await handler.HandleAsync(command, cancellationToken)).ToTypedHttpResult(opened => SessionAnswers.Created(httpContext, opened));
    }

    private static async Task<Results<Accepted, ErrorsProblemHttpResult>> ResendAsync(
        string ownerId, ICommandHandler<ResendOwnerVerification, Unit> handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(new ResendOwnerVerification(ownerId), cancellationToken)).ToTypedHttpResult(_ =>
            TypedResults.Accepted((string?)null));
}
