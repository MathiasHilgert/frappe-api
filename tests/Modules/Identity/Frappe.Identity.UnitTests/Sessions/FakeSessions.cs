using Frappe.Identity.Application.Sessions;
using Frappe.Identity.Domain.Principals;
using Frappe.Identity.Domain.Sessions;

namespace Frappe.Identity.UnitTests.Sessions;

/// <summary>
/// The sessions and the principals they belong to, in memory: what a unit of work would write is what was added or changed, and what the read
/// port projects is built from the same rows, so the rule under test is the real one.
/// </summary>
internal sealed class FakeSessions : ISessions, ISessionReads
{
    public List<Session> Stored { get; } = [];

    public List<Principal> Principals { get; } = [];

    /// <summary>The token hash the last lookup asked for.</summary>
    public byte[]? LookedUp { get; private set; }

    public Task<Session?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Stored.SingleOrDefault(s => s.Id == id));

    public void Add(Session session) => Stored.Add(session);

    public Task<SessionAccess?> FindAccessAsync(byte[] tokenHash, CancellationToken cancellationToken)
    {
        LookedUp = tokenHash;
        var session = Stored.SingleOrDefault(s => s.TokenHash.SequenceEqual(tokenHash));
        var principal = session is null ? null : Principals.Single(p => p.Id == session.PrincipalId);
        return Task.FromResult(session is null || principal is null
            ? null
            : new SessionAccess(
                session.Id, principal.Id, principal.Kind, principal.Status, principal.CredentialStamp, session.CredentialStamp, session.Methods,
                session.LastSeenAt, session.IdleExpiresAt, session.AbsoluteExpiresAt, session.EndedAt, session.RevokedAt, session.StepUpUntil));
    }

    public Task<CurrentSession?> FindCurrentAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Stored.SingleOrDefault(s => s.Id == id) is { } session
            ? new CurrentSession(session.Id, session.IdleExpiresAt, session.AbsoluteExpiresAt, session.StepUpUntil)
            : null);

    /// <summary>An active principal with a live session of the given token hash.</summary>
    public (Principal Principal, Session Session) Seed(byte[] tokenHash, PrincipalStatus status = PrincipalStatus.Active)
    {
        var principal = Principal.Create(PrincipalKind.Owner, status, SessionFixtures.Opened);
        var session = SessionFixtures.SessionOf(principal, tokenHash);
        Principals.Add(principal);
        Stored.Add(session);
        return (principal, session);
    }
}
