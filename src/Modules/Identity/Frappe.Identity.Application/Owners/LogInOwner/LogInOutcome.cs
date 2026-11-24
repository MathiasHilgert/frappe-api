using Frappe.Identity.Application.Sessions;

namespace Frappe.Identity.Application.Owners.LogInOwner;

/// <summary>
/// What a log-in with the right password came to: a session, or, for an owner whose address is not verified, the owner to verify. Both are
/// successes so the unit commits (the new code, or the session).
/// </summary>
public sealed record LogInOutcome
{
    private LogInOutcome(OpenedSession? session, Guid? pendingOwnerId)
    {
        Session = session;
        PendingOwnerId = pendingOwnerId;
    }

    /// <summary>The session that opened; none when verification is pending.</summary>
    public OpenedSession? Session { get; }

    /// <summary>The owner who must verify their address with the code just sent; none when a session opened.</summary>
    public Guid? PendingOwnerId { get; }

    /// <summary>A session opened.</summary>
    public static LogInOutcome Opened(OpenedSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return new LogInOutcome(session, null);
    }

    /// <summary>The owner <paramref name="ownerId"/> must verify their address first.</summary>
    public static LogInOutcome VerificationPending(Guid ownerId) => new(null, ownerId);

    /// <summary>The token is a secret: a session prints without it.</summary>
    public override string ToString() => PendingOwnerId is { } id ? $"LogInOutcome {{ VerificationPending = {id} }}" : "LogInOutcome { Opened }";
}
