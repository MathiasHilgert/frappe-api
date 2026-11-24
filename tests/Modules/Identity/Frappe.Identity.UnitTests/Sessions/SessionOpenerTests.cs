using System.Net;
using Frappe.Identity.Application.Sessions;
using Frappe.Identity.Domain.Sessions;
using Frappe.Identity.Infrastructure.Sessions;
using Microsoft.Extensions.Time.Testing;
using static Frappe.Identity.UnitTests.Sessions.SessionFixtures;

namespace Frappe.Identity.UnitTests.Sessions;

/// <summary>Opening a session stages it with only the hash of its token and returns the token once (S8).</summary>
public sealed class SessionOpenerTests
{
    private static (SessionOpener Opener, FakeSessions Store) OpenerOverFakes()
    {
        var store = new FakeSessions();
        var time = new FakeTimeProvider(Opened.ToDateTimeOffset());
        return (new SessionOpener(store, new SessionTokens(), new SessionLifetimes(Idle, Absolute), time), store);
    }

    [Test]
    public async Task Opening_stages_a_console_session_holding_the_hash_of_the_token_it_returns()
    {
        // Arrange
        var (opener, store) = OpenerOverFakes();
        var principal = Guid.CreateVersion7();
        var stamp = Guid.CreateVersion7();

        // Act
        var opened = opener.Open(principal, stamp, "Mozilla/5.0", IPAddress.Parse("203.0.113.77"));

        // Assert
        var session = store.Stored.Single();
        using (Assert.Multiple())
        {
            await Assert.That(opened.Id).IsEqualTo(session.Id);
            await Assert.That(session.PrincipalId).IsEqualTo(principal);
            await Assert.That(session.CredentialStamp).IsEqualTo(stamp);
            await Assert.That(session.Audience).IsEqualTo(SessionAudience.Console);
            await Assert.That(session.TokenHash.SequenceEqual(new SessionTokens().HashOf(opened.Token.Value)!)).IsTrue();
            await Assert.That(opened.IdleExpiresAt).IsEqualTo(Opened + Idle);
            await Assert.That(opened.AbsoluteExpiresAt).IsEqualTo(Opened + Absolute);
            await Assert.That(session.UserAgent).IsEqualTo("Mozilla/5.0");
            await Assert.That(session.IpPrefix).IsEqualTo(IPNetwork.Parse("203.0.113.0/24"));
        }
    }

    [Test]
    public async Task A_user_agent_longer_than_the_limit_is_cut_to_it()
    {
        // Arrange
        var (opener, store) = OpenerOverFakes();

        // Act
        opener.Open(Guid.CreateVersion7(), Guid.CreateVersion7(), new string('a', Session.UserAgentMaxLength + 100), null);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(store.Stored.Single().UserAgent!.Length).IsEqualTo(Session.UserAgentMaxLength);
            await Assert.That(store.Stored.Single().IpPrefix).IsNull();
        }
    }
}
