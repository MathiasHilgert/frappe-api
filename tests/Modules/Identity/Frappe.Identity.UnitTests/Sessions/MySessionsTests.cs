using Frappe.Identity.Application.Sessions;
using Frappe.Identity.Application.Sessions.ListMySessions;
using Frappe.Identity.Application.Sessions.RevokeMyOtherSessions;
using Frappe.Identity.Application.Sessions.RevokeMySession;
using Frappe.Identity.Domain;
using Frappe.Identity.Domain.Principals;
using Frappe.Identity.Domain.Sessions;
using Frappe.Results;
using Microsoft.Extensions.Time.Testing;
using NodaTime;
using static Frappe.Identity.UnitTests.Sessions.SessionFixtures;

namespace Frappe.Identity.UnitTests.Sessions;

/// <summary>Listing the caller's sessions and revoking one or the others (S13).</summary>
public sealed class MySessionsTests
{
    private const string Chrome = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36";

    private static readonly FakeTimeProvider Now = new(Opened.ToDateTimeOffset() + TimeSpan.FromHours(1));

    private static Session Add(FakeSessions store, Principal principal, Duration openedAfter, string? userAgent = null)
    {
        var session = Session.Open(
            principal.Id, System.Security.Cryptography.RandomNumberGenerator.GetBytes(32), SessionAudience.Console, principal.CredentialStamp,
            Opened + openedAfter, Idle, Absolute, userAgent, System.Net.IPNetwork.Parse("203.0.113.0/24"));
        store.Add(session);
        return session;
    }

    [Test]
    public async Task The_list_shows_the_callers_live_sessions_newest_first_with_the_current_one_marked_and_the_agent_parsed()
    {
        // Arrange
        var store = new FakeSessions();
        var caller = ActivePrincipal();
        var other = ActivePrincipal();
        var oldest = Add(store, caller, Duration.Zero, Chrome);
        var current = Add(store, caller, Duration.FromMinutes(10), "curl/8");
        Add(store, other, Duration.FromMinutes(5), Chrome);
        var handler = new ListMySessionsHandler(store, Now, FakeCaller.Of(caller.Id, current.Id));

        // Act
        var result = await handler.HandleAsync(new ListMySessions(50, null), default);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.IsSuccess).IsTrue();
            await Assert.That(result.Value.Items.Select(i => i.Id).ToArray()).IsEquivalentTo([current.Id, oldest.Id]);
            await Assert.That(result.Value.Items.Select(i => i.Current).ToArray()).IsEquivalentTo([true, false]);
            await Assert.That(result.Value.Items[1].Browser).IsEqualTo("Chrome");
            await Assert.That(result.Value.Items[1].Os).IsEqualTo("Windows");
            await Assert.That(result.Value.Items[0].Browser).IsNull();
            await Assert.That(result.Value.Items[1].IpPrefix!.ToString()).IsEqualTo("203.0.113.0/24");
            await Assert.That(result.Value.Items[1].OpenedAt).IsEqualTo(Opened);
            await Assert.That(result.Value.Items[1].LastSeenAt).IsEqualTo(Opened);
            await Assert.That(result.Value.Next).IsNull();
        }
    }

    [Test]
    public async Task The_list_skips_revoked_and_expired_sessions()
    {
        // Arrange
        var store = new FakeSessions();
        var caller = ActivePrincipal();
        var live = Add(store, caller, Duration.FromMinutes(1));
        Add(store, caller, Duration.FromMinutes(2)).Revoke(RevocationReason.Logout, Opened + Duration.FromMinutes(3));
        Add(store, caller, -Duration.FromHours(20));
        var handler = new ListMySessionsHandler(store, Now, FakeCaller.Of(caller.Id, live.Id));

        // Act
        var result = await handler.HandleAsync(new ListMySessions(50, null), default);

        // Assert
        await Assert.That(result.Value.Items.Select(i => i.Id).ToArray()).IsEquivalentTo([live.Id]);
    }

    [Test]
    public async Task A_page_ends_at_the_limit_and_the_next_one_starts_right_after_it_without_repeating_or_skipping()
    {
        // Arrange
        var store = new FakeSessions();
        var caller = ActivePrincipal();
        var sessions = Enumerable.Range(1, 5).Select(i => Add(store, caller, Duration.FromMinutes(i))).Reverse().ToArray();
        var handler = new ListMySessionsHandler(store, Now, FakeCaller.Of(caller.Id, sessions[0].Id));

        // Act
        var first = (await handler.HandleAsync(new ListMySessions(2, null), default)).Value;
        var second = (await handler.HandleAsync(new ListMySessions(2, first.Next), default)).Value;
        var third = (await handler.HandleAsync(new ListMySessions(2, second.Next), default)).Value;

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(first.Items.Select(i => i.Id).ToArray()).IsEquivalentTo(sessions[..2].Select(s => s.Id).ToArray());
            await Assert.That(first.Next).IsEqualTo(new SessionPosition(sessions[1].OpenedAt, sessions[1].Id));
            await Assert.That(second.Items.Select(i => i.Id).ToArray()).IsEquivalentTo(sessions[2..4].Select(s => s.Id).ToArray());
            await Assert.That(third.Items.Select(i => i.Id).ToArray()).IsEquivalentTo([sessions[4].Id]);
            await Assert.That(third.Next).IsNull();
        }
    }

    [Test]
    public async Task Revoking_one_of_the_callers_sessions_revokes_it_for_the_holder_and_raises_that_it_was_revoked()
    {
        // Arrange
        var store = new FakeSessions();
        var caller = ActivePrincipal();
        var session = Add(store, caller, Duration.FromMinutes(1));
        session.DequeueDomainEvents();
        var handler = new RevokeMySessionHandler(store, Now);

        // Act
        var result = await handler.HandleAsync(new RevokeMySession(caller.Id, session.Id), default);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.IsSuccess).IsTrue();
            await Assert.That(session.RevocationReason).IsEqualTo(RevocationReason.RevokedByHolder);
            await Assert.That(session.DomainEvents.Single())
                .IsEqualTo(new SessionRevoked(caller.Id, session.Id, RevocationReason.RevokedByHolder, Instant.FromDateTimeOffset(Now.GetUtcNow())));
        }
    }

    [Test]
    public async Task Another_principals_session_a_session_already_over_and_an_unknown_one_are_the_same_not_found_and_change_nothing()
    {
        // Arrange
        var store = new FakeSessions();
        var caller = ActivePrincipal();
        var foreign = Add(store, ActivePrincipal(), Duration.FromMinutes(1));
        var over = Add(store, caller, Duration.FromMinutes(2));
        over.Revoke(RevocationReason.Logout, Opened + Duration.FromMinutes(3));
        var handler = new RevokeMySessionHandler(store, Now);

        // Act
        var results = new[]
        {
            await handler.HandleAsync(new RevokeMySession(caller.Id, foreign.Id), default),
            await handler.HandleAsync(new RevokeMySession(caller.Id, over.Id), default),
            await handler.HandleAsync(new RevokeMySession(caller.Id, Guid.NewGuid()), default),
            await handler.HandleAsync(new RevokeMySession(caller.Id, Guid.Empty), default),
        };

        // Assert
        using (Assert.Multiple())
        {
            foreach (var result in results)
            {
                await Assert.That(result.Errors.Single()).IsEqualTo(new NotFoundError(IdentityErrorCodes.SessionNotFound));
            }

            await Assert.That(foreign.RevokedAt).IsNull();
            await Assert.That(over.RevocationReason).IsEqualTo(RevocationReason.Logout);
        }
    }

    [Test]
    public async Task Revoking_the_others_keeps_the_current_session_and_touches_no_other_principals_session()
    {
        // Arrange
        var store = new FakeSessions();
        var caller = ActivePrincipal();
        var current = Add(store, caller, Duration.FromMinutes(1));
        var first = Add(store, caller, Duration.FromMinutes(2));
        var second = Add(store, caller, Duration.FromMinutes(3));
        var foreign = Add(store, ActivePrincipal(), Duration.FromMinutes(4));
        var handler = new RevokeMyOtherSessionsHandler(store, Now, FakeCaller.Of(caller.Id, current.Id));

        // Act
        var result = await handler.HandleAsync(new RevokeMyOtherSessions(), default);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.IsSuccess).IsTrue();
            await Assert.That(current.RevokedAt).IsNull();
            await Assert.That(first.RevocationReason).IsEqualTo(RevocationReason.OthersRevoked);
            await Assert.That(second.RevocationReason).IsEqualTo(RevocationReason.OthersRevoked);
            await Assert.That(foreign.RevokedAt).IsNull();
        }
    }

    [Test]
    public async Task Revoking_the_others_with_no_other_session_succeeds()
    {
        // Arrange
        var store = new FakeSessions();
        var caller = ActivePrincipal();
        var current = Add(store, caller, Duration.FromMinutes(1));
        var handler = new RevokeMyOtherSessionsHandler(store, Now, FakeCaller.Of(caller.Id, current.Id));

        // Act
        var result = await handler.HandleAsync(new RevokeMyOtherSessions(), default);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.IsSuccess).IsTrue();
            await Assert.That(current.RevokedAt).IsNull();
        }
    }
}
