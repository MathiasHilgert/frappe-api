using Frappe.Results;
using Frappe.UseCases;
using NodaTime;

namespace Frappe.Identity.Application.Sessions.TouchSession;

/// <summary>Handles <see cref="TouchSession"/>: a session that is gone or already revoked stays as it is.</summary>
internal sealed class TouchSessionHandler(ISessions sessions, SessionLifetimes lifetimes, TimeProvider time) : ICommandHandler<TouchSession, Unit>
{
    public async Task<Result<Unit>> HandleAsync(TouchSession command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var session = await sessions.FindAsync(command.SessionId, cancellationToken);
        session?.Touch(Instant.FromDateTimeOffset(time.GetUtcNow()), lifetimes.Idle);
        return Unit.Value;
    }
}
