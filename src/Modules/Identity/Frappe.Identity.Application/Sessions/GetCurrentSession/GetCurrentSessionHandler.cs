using Frappe.Access.Contracts;
using Frappe.Identity.Domain;
using Frappe.Results;
using Frappe.UseCases;
using NodaTime;

namespace Frappe.Identity.Application.Sessions.GetCurrentSession;

/// <summary>
/// Handles <see cref="GetCurrentSession"/>; a session that vanished since the request authenticated is answered like any invalid one.
/// </summary>
internal sealed class GetCurrentSessionHandler(ISessionReads reads, TimeProvider time, ICaller caller)
    : IQueryHandler<GetCurrentSession, CurrentSessionView>
{
    public async Task<Result<CurrentSessionView>> HandleAsync(GetCurrentSession query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var session = await reads.FindCurrentAsync(caller.RequiredSessionId, cancellationToken);
        if (session is null)
        {
            return new UnauthenticatedError(IdentityErrorCodes.SessionInvalid);
        }

        var now = Instant.FromDateTimeOffset(time.GetUtcNow());
        return new CurrentSessionView(
            session.Id, session.IdleExpiresAt, session.AbsoluteExpiresAt, session.StepUpUntil is { } until && until > now ? until : null);
    }
}
