using System.Text.Json.Serialization;
using Frappe.Identity.Application.Sessions;
using Frappe.Identity.Domain;
using Frappe.Identity.Infrastructure.Sessions;
using Frappe.SharedKernel.Validation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Wolverine;

namespace Frappe.Identity.Infrastructure.Endpoints;

internal static class SessionManagementEndpoints
{
    public static RouteGroupBuilder MapSessionManagement(this RouteGroupBuilder group)
    {
        var sessions = group.MapGroup("/sessions").RequireAuthorization(IdentityPolicies.OwnerSelf);
        sessions.MapGet("/", ListAsync).WithName("ListSessions");
        sessions.MapDelete("/{id:guid}", RevokeAsync).WithName("RevokeSession");
        return group;
    }

    private static async Task<Results<Ok<IReadOnlyList<SessionResponse>>, UnauthorizedHttpResult>> ListAsync(
        ICurrentPrincipal current, IMessageBus bus, CancellationToken cancellationToken)
    {
        if (current.Session is not { } caller)
        {
            return TypedResults.Unauthorized();
        }

        var list = await bus.InvokeAsync<IReadOnlyList<SessionSummary>>(
            new ListOwnerSessions(caller.PrincipalId, caller.SessionId), cancellationToken);
        return TypedResults.Ok<IReadOnlyList<SessionResponse>>(
            list.Select(s => new SessionResponse(s.Id, s.CreatedAt, s.LastSeenAt, s.Current)).ToList());
    }

    private static async Task<Results<NoContent, NotFound, UnauthorizedHttpResult>> RevokeAsync(
        Guid id, ICurrentPrincipal current, IMessageBus bus, CancellationToken cancellationToken)
    {
        if (current.Session is not { } caller)
        {
            return TypedResults.Unauthorized();
        }

        var revoked = await bus.InvokeAsync<Result>(new RevokeSession(caller.PrincipalId, id), cancellationToken);
        if (revoked.IsSuccess)
        {
            return TypedResults.NoContent();
        }

        return revoked.Errors.Contains(IdentityErrors.SessionNotFound)
            ? TypedResults.NotFound()
            : throw new InvalidOperationException($"Unexpected failure revoking a session: {revoked.Errors[0].Code}.");
    }

    internal sealed record SessionResponse(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
        [property: JsonPropertyName("last_seen_at")] DateTimeOffset LastSeenAt,
        [property: JsonPropertyName("current")] bool Current);
}
