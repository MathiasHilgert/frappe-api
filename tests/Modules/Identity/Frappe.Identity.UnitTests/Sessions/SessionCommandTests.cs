using Frappe.Identity.Application.Sessions;
using Frappe.Identity.Application.Sessions.GetCurrentSession;
using Frappe.Identity.Application.Sessions.LogOut;
using Frappe.Identity.Application.Sessions.TouchSession;
using Frappe.Identity.Domain;
using Frappe.Identity.Domain.Sessions;
using Frappe.Results;
using Microsoft.Extensions.Time.Testing;
using NodaTime;
using static Frappe.Identity.UnitTests.Sessions.SessionFixtures;

namespace Frappe.Identity.UnitTests.Sessions;

/// <summary>Touching a session, logging out of it and reading it (S8, S9).</summary>
public sealed class SessionCommandTests
{
    private static readonly SessionLifetimes Lifetimes = new(Idle, Absolute);

    private static FakeTimeProvider At(Duration after) => new(Opened.ToDateTimeOffset() + after.ToTimeSpan());

    [Test]
    public async Task Touching_a_session_records_the_use_and_moves_its_idle_expiry_on()
    {
        // Arrange
        var store = new FakeSessions();
        var (_, session) = store.Seed(new byte[32]);
        var handler = new TouchSessionHandler(store, Lifetimes, At(Duration.FromHours(3)));

        // Act
        var result = await handler.HandleAsync(new TouchSession(session.Id), default);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.IsSuccess).IsTrue();
            await Assert.That(session.LastSeenAt).IsEqualTo(Opened + Duration.FromHours(3));
            await Assert.That(session.IdleExpiresAt).IsEqualTo(Opened + Duration.FromHours(3) + Idle);
        }
    }

    [Test]
    public async Task Touching_a_session_that_is_gone_succeeds_and_changes_nothing()
    {
        // Arrange
        var store = new FakeSessions();
        var handler = new TouchSessionHandler(store, Lifetimes, At(Duration.FromHours(3)));

        // Act
        var result = await handler.HandleAsync(new TouchSession(Guid.CreateVersion7()), default);

        // Assert
        await Assert.That(result.IsSuccess).IsTrue();
    }

    [Test]
    public async Task Logging_out_revokes_the_callers_session_for_logout_and_raises_that_it_closed()
    {
        // Arrange
        var store = new FakeSessions();
        var (principal, session) = store.Seed(new byte[32]);
        session.DequeueDomainEvents();
        var handler = new LogOutHandler(store, At(Duration.FromMinutes(5)));

        // Act
        var result = await handler.HandleAsync(new LogOut(principal.Id, session.Id), default);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.IsSuccess).IsTrue();
            await Assert.That(session.RevocationReason).IsEqualTo(RevocationReason.Logout);
            await Assert.That(session.RevokedAt).IsEqualTo(Opened + Duration.FromMinutes(5));
            await Assert.That(session.DomainEvents.Single()).IsEqualTo(new SessionClosed(principal.Id, session.Id, Opened + Duration.FromMinutes(5)));
        }
    }

    [Test]
    public async Task Logging_out_of_a_session_that_is_not_the_callers_changes_nothing()
    {
        // Arrange
        var store = new FakeSessions();
        var (_, session) = store.Seed(new byte[32]);
        var handler = new LogOutHandler(store, At(Duration.FromMinutes(5)));

        // Act
        var result = await handler.HandleAsync(new LogOut(Guid.CreateVersion7(), session.Id), default);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.IsSuccess).IsTrue();
            await Assert.That(session.RevokedAt).IsNull();
        }
    }

    [Test]
    public async Task Logging_out_twice_succeeds_both_times()
    {
        // Arrange
        var store = new FakeSessions();
        var (principal, session) = store.Seed(new byte[32]);
        var handler = new LogOutHandler(store, At(Duration.FromMinutes(5)));
        await handler.HandleAsync(new LogOut(principal.Id, session.Id), default);

        // Act
        var again = await handler.HandleAsync(new LogOut(principal.Id, session.Id), default);

        // Assert
        await Assert.That(again.IsSuccess).IsTrue();
    }

    [Test]
    public async Task The_current_session_says_when_it_idles_out_ends_and_how_long_its_step_up_counts_only_while_it_does()
    {
        // Arrange
        var store = new FakeSessions();
        var (_, session) = store.Seed(new byte[32]);
        var handler = new GetCurrentSessionHandler(store, At(Duration.FromMinutes(5)));

        // Act
        var result = await handler.HandleAsync(new GetCurrentSession(session.Id), default);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.Value).IsEqualTo(new CurrentSessionView(session.Id, Opened + Idle, Opened + Absolute, null));
        }
    }

    [Test]
    public async Task A_step_up_that_still_counts_is_reported_and_one_that_ran_out_is_not()
    {
        // Arrange
        var id = Guid.CreateVersion7();
        var counting = new StepUpReads(new CurrentSession(id, Opened + Idle, Opened + Absolute, Opened + Duration.FromMinutes(10)));
        var ranOut = new StepUpReads(new CurrentSession(id, Opened + Idle, Opened + Absolute, Opened + Duration.FromMinutes(2)));
        var now = At(Duration.FromMinutes(5));

        // Act
        var whileCounting = await new GetCurrentSessionHandler(counting, now).HandleAsync(new GetCurrentSession(id), default);
        var afterwards = await new GetCurrentSessionHandler(ranOut, now).HandleAsync(new GetCurrentSession(id), default);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(whileCounting.Value.StepUpValidUntil).IsEqualTo(Opened + Duration.FromMinutes(10));
            await Assert.That(afterwards.Value.StepUpValidUntil).IsNull();
        }
    }

    [Test]
    public async Task A_current_session_that_vanished_is_the_one_invalid_session_answer()
    {
        // Arrange
        var handler = new GetCurrentSessionHandler(new FakeSessions(), At(Duration.Zero));

        // Act
        var result = await handler.HandleAsync(new GetCurrentSession(Guid.CreateVersion7()), default);

        // Assert
        await Assert.That(result.Errors.Single()).IsEqualTo(new UnauthenticatedError(IdentityErrorCodes.SessionInvalid));
    }

    /// <summary>A read port that answers one session, with a step-up the domain cannot grant yet.</summary>
    private sealed class StepUpReads(CurrentSession current) : ISessionReads
    {
        public Task<Frappe.Identity.Domain.Sessions.SessionAccess?> FindAccessAsync(byte[] tokenHash, CancellationToken cancellationToken) =>
            Task.FromResult<Frappe.Identity.Domain.Sessions.SessionAccess?>(null);

        public Task<CurrentSession?> FindCurrentAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<CurrentSession?>(current);
    }
}
