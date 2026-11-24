using System.Net;
using Frappe.Identity.Domain.Sessions;
using NodaTime;

namespace Frappe.Identity.Application.Sessions;

/// <summary>
/// Opens a session for a principal (S8): a new token, whose hash is staged with the session in the caller's unit of work, so the session
/// exists exactly when the command that opened it commits. It judges nothing about the principal: whoever calls it has already proved who they
/// are and passes the credential stamp the proof was made under. The user agent is kept as sent up to its limit; the address only as its
/// IPv4 /24 or IPv6 /48.
/// </summary>
public sealed class SessionOpener(ISessions sessions, ISessionTokens tokens, SessionLifetimes lifetimes, TimeProvider time)
{
    /// <summary>Stages a console session of <paramref name="principalId"/>, opened under <paramref name="credentialStamp"/>.</summary>
    public OpenedSession Open(Guid principalId, Guid credentialStamp, string? userAgent, IPAddress? address)
    {
        var token = tokens.Issue();
        var session = Session.Open(
            principalId, token.Hash, SessionAudience.Console, credentialStamp, Instant.FromDateTimeOffset(time.GetUtcNow()), lifetimes.Idle,
            lifetimes.Absolute, Truncated(userAgent), ClientNetwork.PrefixOf(address));
        sessions.Add(session);
        return new OpenedSession(session.Id, token, session.IdleExpiresAt, session.AbsoluteExpiresAt);
    }

    private static string? Truncated(string? userAgent) =>
        userAgent is { Length: > Session.UserAgentMaxLength } ? userAgent[..Session.UserAgentMaxLength] : userAgent;
}
