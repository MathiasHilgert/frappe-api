using Frappe.Http;
using Frappe.Identity.Application.Credentials;
using Frappe.Identity.Application.Owners.UndoEmailChange;
using Frappe.Results;
using Frappe.UseCases;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Frappe.Identity.Http.Owners;

/// <summary>
/// Undoing an email change from the notice (S15): <c>POST /email-change-undos</c> spends the token the notice to the old address carried. It is
/// anonymous (whoever has the old mailbox holds the token, not a session) and never cached; it has nothing to follow, so it answers no
/// <c>Location</c>.
/// </summary>
internal static class EmailChangeUndoEndpoints
{
    extension(IEndpointRouteBuilder routes)
    {
        public void MapEmailChangeUndoEndpoints() =>
            routes.MapPost("/email-change-undos", UndoAsync)
                .WithName("UndoEmailChange").WithTags(IdentityHttp.Tag).NoStore().WithoutLocation()
                .RequireRateLimiting(IdentityHttp.EmailChangeUndoRateLimitPolicy)
                .WithSummary("Undo an email change from the notice")
                .WithDescription(
                    "Restores the earlier address as verified, using the single-use token in the link of the notice mailed to that "
                    + "address (valid 7 days): every session of the owner is revoked, so the owner logs in again, and every "
                    + "live code of the owner stops working. A change made later has its own token, which stops working too. 422 "
                    + "identity.undo.invalid, one answer and one time (never sooner than "
                    + $"{EmailChangeUndoLimits.Floor.TotalMilliseconds:0} ms), for a token that is malformed, unknown, wrong, expired, used or "
                    + "replaced and for an old address that another verified owner holds now. 429 http.rate_limited with Retry-After.")
                .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
                .ProducesProblem(StatusCodes.Status429TooManyRequests);
    }

    private static async Task<Results<NoContent, ErrorsProblemHttpResult>> UndoAsync(
        [FromBody] UndoEmailChangeRequest? body, ICommandHandler<UndoEmailChange, Unit> handler, TimeProvider time,
        CancellationToken cancellationToken)
    {
        // The floor starts before the handler and ends after it, and its unit of work committed with it: a good token writes, a refusal does not.
        var started = time.GetTimestamp();
        var result = await handler.HandleAsync(new UndoEmailChange(body?.Token), cancellationToken);
        await ResponseFloor.HoldAsync(time, started, EmailChangeUndoLimits.Floor, cancellationToken);
        return result.ToTypedHttpResult(_ => TypedResults.NoContent());
    }
}
