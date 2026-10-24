using Frappe.Identity.Application.Sessions;
using Frappe.Identity.Infrastructure.Sessions;
using Frappe.Platform.Web.Problems;
using Frappe.SharedKernel.Validation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Wolverine;

namespace Frappe.Identity.Infrastructure.Endpoints;

/// <summary>The signed-in owner's open sessions: list them and sign any one of them out.</summary>
internal static class SessionManagementEndpoints
{
    public static RouteGroupBuilder MapSessionManagementEndpoints(this RouteGroupBuilder group)
    {
        var sessions = group.MapGroup("/sessions").RequireOwner();
        sessions.MapGet("/", ListAsync)
            .WithName("ListSessions")
            .WithSummary("The owner's open sessions, flagging the caller's own.");
        sessions.MapDelete("/{id:guid}", RevokeAsync)
            .WithName("RevokeSession")
            .WithSummary("Signs one of the owner's sessions out.")
            .ProducesProblem(StatusCodes.Status404NotFound);
        return group;
    }

    private static async Task<Ok<IReadOnlyList<SessionSummaryResponse>>> ListAsync(
        ICurrentPrincipal current, IMessageBus bus, CancellationToken cancellationToken)
    {
        var caller = current.RequiredSession();
        var list = await bus.InvokeAsync<IReadOnlyList<SessionSummary>>(
            new ListOwnerSessions(caller.PrincipalId, caller.SessionId), cancellationToken);
        return TypedResults.Ok<IReadOnlyList<SessionSummaryResponse>>(
            [.. list.Select(s => new SessionSummaryResponse(s.Id, s.CreatedAt, s.LastSeenAt, s.Current))]);
    }

    private static async Task<Results<NoContent, LocalizedProblemResult>> RevokeAsync(
        Guid id, ICurrentPrincipal current, IMessageBus bus, CancellationToken cancellationToken)
    {
        var revoked = await bus.InvokeAsync<Result>(new RevokeSession(current.RequiredSession().PrincipalId, id), cancellationToken);
        return revoked.IsSuccess ? TypedResults.NoContent() : revoked.ToProblem();
    }
}
