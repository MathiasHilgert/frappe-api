using Frappe.Access.Contracts;
using Frappe.Identity.Domain.Sessions;
using Frappe.Results;
using Frappe.UseCases;
using NodaTime;

namespace Frappe.Identity.Application.Sessions.LogOut;

/// <summary>Handles <see cref="LogOut"/>.</summary>
internal sealed class LogOutHandler(ISessions sessions, TimeProvider time, ICaller caller) : ICommandHandler<LogOut, Unit>
{
    public async Task<Result<Unit>> HandleAsync(LogOut command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var session = await sessions.FindAsync(caller.RequiredSessionId, cancellationToken);
        if (session is not null && session.PrincipalId == caller.RequiredPrincipalId)
        {
            session.Revoke(RevocationReason.Logout, Instant.FromDateTimeOffset(time.GetUtcNow()));
        }

        return Unit.Value;
    }
}
