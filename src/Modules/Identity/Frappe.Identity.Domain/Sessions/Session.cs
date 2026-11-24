using System.Net;
using Frappe.Aggregates;
using Frappe.Identity.Domain.Principals;
using NodaTime;

// EF Core materializes the aggregate through its private constructor and setters.
#pragma warning disable S1144, S3453
namespace Frappe.Identity.Domain.Sessions;

/// <summary>
/// One sign-in of a principal on one client: independent of the principal and of every other session (S2). It holds the SHA-256 of its token
/// (never the token), the credential stamp it was opened under, and the instants that decide whether it is alive.
/// </summary>
public sealed class Session : AggregateRoot
{
    /// <summary>The longest user agent kept ("El texto completo"): the length the column allows.</summary>
    public const int UserAgentMaxLength = 512;

    /// <summary>How long a step-up holds, counted from its completion: use never extends it.</summary>
    public static readonly Duration StepUpWindow = Duration.FromMinutes(5);

    private Session()
    {
        TokenHash = null!;
        Methods = null!;
    }

    private Session(Guid id, Guid principalId, byte[] tokenHash, SessionAudience audience, Guid credentialStamp, Instant now,
        Duration idleTimeout, Duration absoluteTimeout, string? userAgent, IPNetwork? ipPrefix)
    {
        Id = id;
        PrincipalId = principalId;
        TokenHash = tokenHash;
        Audience = audience;
        Methods = [AuthenticationMethod.Pwd];
        CredentialStamp = credentialStamp;
        OpenedAt = now;
        LastSeenAt = now;
        AbsoluteExpiresAt = now + absoluteTimeout;
        IdleExpiresAt = Instant.Min(now + idleTimeout, AbsoluteExpiresAt);
        UserAgent = userAgent;
        IpPrefix = ipPrefix;
        Raise(new SessionOpened(principalId, id, audience, now));
    }

    /// <summary>UUIDv7 made by the application (DATA-02).</summary>
    public Guid Id { get; private set; }

    public Guid PrincipalId { get; private set; }

    /// <summary>The SHA-256 of the 32 random bytes of the token.</summary>
    public byte[] TokenHash { get; private set; }

    public SessionAudience Audience { get; private set; }

    /// <summary>How the holder proved who they are; never empty.</summary>
    public IReadOnlyList<AuthenticationMethod> Methods { get; private set; }

    /// <summary>The principal's credential stamp when the session opened; a session whose stamp differs is no longer valid.</summary>
    public Guid CredentialStamp { get; private set; }

    public Instant OpenedAt { get; private set; }

    public Instant LastSeenAt { get; private set; }

    /// <summary>The session idles out when it was not used by then.</summary>
    public Instant IdleExpiresAt { get; private set; }

    /// <summary>The session ends by then, however much it is used.</summary>
    public Instant AbsoluteExpiresAt { get; private set; }

    /// <summary>When the session ended: the sweep sets it once the session expired or was revoked. The purge counts from it.</summary>
    public Instant? EndedAt { get; private set; }

    /// <summary>The client's user agent as sent, up to 512 characters; browser and OS are parsed only when sessions are listed.</summary>
    public string? UserAgent { get; private set; }

    /// <summary>The client address truncated to its IPv4 /24 or IPv6 /48, the only form kept.</summary>
    public IPNetwork? IpPrefix { get; private set; }

    /// <summary>Until when a recent password check allows a sensitive action; empty when none was made.</summary>
    public Instant? StepUpUntil { get; private set; }

    public Instant? RevokedAt { get; private set; }

    public RevocationReason? RevocationReason { get; private set; }

    /// <summary>Opens a session of <paramref name="principalId"/> authenticated by password at <paramref name="now"/>.</summary>
    public static Session Open(
        Guid principalId, byte[] tokenHash, SessionAudience audience, Guid credentialStamp, Instant now, Duration idleTimeout,
        Duration absoluteTimeout, string? userAgent, IPNetwork? ipPrefix)
    {
        ArgumentNullException.ThrowIfNull(tokenHash);
        return new Session(
            Guid.CreateVersion7(), principalId, tokenHash, audience, credentialStamp, now, idleTimeout, absoluteTimeout, userAgent, ipPrefix);
    }

    /// <summary>
    /// Whether this session lets <paramref name="principal"/> in at <paramref name="now"/>: it is the principal's own, not ended or revoked,
    /// neither expiry reached, the principal active and its credential stamp still the one the session was opened under.
    /// </summary>
    public bool IsActiveFor(Instant now, Principal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        return principal.Id == PrincipalId && AccessFor(principal).IsActiveAt(now);
    }

    /// <summary>Whether the session is neither revoked nor ended and has reached neither expiry at <paramref name="now"/>.</summary>
    public bool IsLiveAt(Instant now) => RevokedAt is null && EndedAt is null && now < IdleExpiresAt && now < AbsoluteExpiresAt;

    /// <summary>
    /// Records that the session was used at <paramref name="now"/> and pushes its idle expiry to <paramref name="idleTimeout"/> after it,
    /// never past the absolute expiry and never earlier than it already was. A session that is revoked or ended is left alone.
    /// </summary>
    public void Touch(Instant now, Duration idleTimeout)
    {
        if (RevokedAt is not null || EndedAt is not null)
        {
            return;
        }

        LastSeenAt = Instant.Max(LastSeenAt, now);
        IdleExpiresAt = Instant.Max(IdleExpiresAt, Instant.Min(now + idleTimeout, AbsoluteExpiresAt));
    }

    /// <summary>
    /// Revokes the session for <paramref name="reason"/> at <paramref name="now"/>; doing it again, or on an ended session, changes nothing.
    /// A logout raises <see cref="SessionClosed"/>, any other reason <see cref="SessionRevoked"/>.
    /// </summary>
    public void Revoke(RevocationReason reason, Instant now)
    {
        if (RevokedAt is not null || EndedAt is not null)
        {
            return;
        }

        RevokedAt = now;
        RevocationReason = reason;
        Raise(reason == Sessions.RevocationReason.Logout
            ? new SessionClosed(PrincipalId, Id, now)
            : new SessionRevoked(PrincipalId, Id, reason, now));
    }

    /// <summary>
    /// Records that the holder proved the password at <paramref name="now"/>: a sensitive action is allowed for <see cref="StepUpWindow"/> from
    /// it. A later step-up starts a new window; using the session never moves it. A session that is revoked or ended is left alone and
    /// <see langword="false"/> is answered.
    /// </summary>
    public bool StepUp(Instant now)
    {
        if (RevokedAt is not null || EndedAt is not null)
        {
            return false;
        }

        StepUpUntil = now + StepUpWindow;
        Raise(new StepUpCompleted(PrincipalId, Id, StepUpUntil.Value, now));
        return true;
    }

    /// <summary>
    /// Opens the session under <paramref name="stamp"/>, the principal's new credential stamp, so a credential change the holder made from
    /// this session does not end it. A session that is revoked or ended stays as it is.
    /// </summary>
    public void Restamp(Guid stamp)
    {
        if (RevokedAt is not null || EndedAt is not null)
        {
            return;
        }

        CredentialStamp = stamp;
    }

    private SessionAccess AccessFor(Principal principal) => new(
        Id, PrincipalId, principal.Kind, principal.Status, principal.CredentialStamp, CredentialStamp, Methods, LastSeenAt, IdleExpiresAt,
        AbsoluteExpiresAt, EndedAt, RevokedAt, StepUpUntil);
}
