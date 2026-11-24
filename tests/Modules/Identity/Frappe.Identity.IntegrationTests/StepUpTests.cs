using Frappe.Identity.Application.Credentials;
using Frappe.Identity.Application.Sessions;
using Frappe.Identity.Domain;
using Frappe.Results;
using Frappe.Testing.Containers;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace Frappe.Identity.IntegrationTests;

/// <summary>
/// Stepping up the current session on a real Postgres (S14): the password is rechecked under its own throttle, the window is five minutes from
/// completion and use never extends it, and every outcome is counted.
/// </summary>
[ClassDataSource<PostgresServer>(Shared = SharedType.PerTestSession)]
[NotInParallel]
public sealed class StepUpTests(PostgresServer server)
{
    private static async Task<(Guid Id, OpenedSession Session)> ActiveOwnerAsync(OwnersHost host)
    {
        var registered = await host.RegisterAsync();
        var verified = await host.VerifyAsync(registered.Value.Id.ToString(), await host.LastCodeAsync());
        await Assert.That(verified.IsSuccess).IsTrue();
        return (registered.Value.Id, verified.Value);
    }

    [Test]
    public async Task The_right_password_opens_a_five_minute_window_that_the_session_reports_and_that_runs_out_with_time()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);
            var (id, session) = await ActiveOwnerAsync(host);
            using var counts = new Counts();

            // Act
            var asked = Instant.FromDateTimeOffset(host.Time.GetUtcNow());
            var stepped = await host.StepUpAsync(id, session.Id, OwnersHost.Password);
            var answered = Instant.FromDateTimeOffset(host.Time.GetUtcNow());
            var expected = stepped.Value.ValidUntil;
            var before = await host.CurrentAsync(session.Id);
            host.Time.SetUtcNow(expected.ToDateTimeOffset());
            var after = await host.CurrentAsync(session.Id);

            // Assert
            var events = (await host.OutboxAsync()).Where(e => e.Type == "frappe.identity.step_up_completed.v1").ToList();
            using (Assert.Multiple())
            {
                await Assert.That(stepped.IsSuccess).IsTrue();
                await Assert.That(expected).IsGreaterThanOrEqualTo(asked + Duration.FromMinutes(5));
                await Assert.That(expected).IsLessThanOrEqualTo(answered + Duration.FromMinutes(5));
                await Assert.That(before.Value.StepUpValidUntil).IsEqualTo(expected);
                await Assert.That(after.Value.StepUpValidUntil).IsNull();
                await Assert.That(events).HasSingleItem();
                await Assert.That(events[0].Data.GetProperty("sessionId").GetGuid()).IsEqualTo(session.Id);
                await Assert.That(events[0].Data.GetProperty("principalId").GetGuid()).IsEqualTo(id);
                await Assert.That(counts.Of("frappe.identity.step_ups", "completed")).IsEqualTo(1);
            }
        });
    }

    [Test]
    public async Task Using_the_session_never_extends_the_window()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);
            var (id, session) = await ActiveOwnerAsync(host);
            var stepped = await host.StepUpAsync(id, session.Id, OwnersHost.Password);

            // Act
            host.Time.Advance(TimeSpan.FromMinutes(4));
            var letsIn = await host.LetsInAsync(session);
            var middle = await host.CurrentAsync(session.Id);
            host.Time.Advance(TimeSpan.FromMinutes(1));
            var end = await host.CurrentAsync(session.Id);

            // Assert
            await using var db = host.Context();
            var stored = await db.Sessions.AsNoTracking().SingleAsync(s => s.Id == session.Id);
            using (Assert.Multiple())
            {
                await Assert.That(letsIn).IsTrue();
                await Assert.That(middle.Value.StepUpValidUntil).IsEqualTo(stepped.Value.ValidUntil);
                await Assert.That(end.Value.StepUpValidUntil).IsNull();
                await Assert.That(stored.StepUpUntil).IsEqualTo(stepped.Value.ValidUntil);
            }
        });
    }

    [Test]
    public async Task A_wrong_password_is_the_422_code_and_opens_no_window_and_says_nothing()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);
            var (id, session) = await ActiveOwnerAsync(host);
            using var counts = new Counts();

            // Act
            var wrong = await host.StepUpAsync(id, session.Id, "not my password!");
            var current = await host.CurrentAsync(session.Id);

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(wrong.Errors.Single().Code).IsEqualTo(IdentityErrorCodes.PasswordIncorrect);
                await Assert.That(wrong.Errors.Single()).IsTypeOf<RuleViolationError>();
                await Assert.That(current.Value.StepUpValidUntil).IsNull();
                await Assert.That((await host.OutboxAsync()).Any(e => e.Type == "frappe.identity.step_up_completed.v1")).IsFalse();
                await Assert.That(counts.Of("frappe.identity.step_ups", "incorrect")).IsEqualTo(1);
                await Assert.That(counts.Of("frappe.identity.step_ups", "completed")).IsEqualTo(0);
            }
        });
    }

    [Test]
    public async Task The_password_is_rechecked_ten_times_per_principal_then_throttled_even_when_right()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);
            var (id, session) = await ActiveOwnerAsync(host);
            for (var i = 0; i < PasswordRecheck.Flow.Limit; i++)
            {
                await host.StepUpAsync(id, session.Id, "not my password!");
            }

            using var counts = new Counts();

            // Act
            var throttled = await host.StepUpAsync(id, session.Id, OwnersHost.Password);
            var current = await host.CurrentAsync(session.Id);

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(throttled.Errors.Single()).IsTypeOf<RateLimitedError>();
                await Assert.That(host.Limiter.HitsOf(PasswordRecheck.Flow.Name, id.ToString("N"))).IsEqualTo(PasswordRecheck.Flow.Limit + 1);
                await Assert.That(current.Value.StepUpValidUntil).IsNull();
                await Assert.That(counts.Of("frappe.identity.step_ups", "throttled")).IsEqualTo(1);
            }
        });
    }

    [Test]
    public async Task While_rechecks_cannot_be_counted_no_password_is_judged_and_no_window_opens()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);
            var (id, session) = await ActiveOwnerAsync(host);
            host.Limiter.Down = true;

            // Act
            var result = await host.StepUpAsync(id, session.Id, OwnersHost.Password);

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(result.Errors.Single()).IsTypeOf<UnavailableError>();
                await Assert.That((await host.CurrentAsync(session.Id)).Value.StepUpValidUntil).IsNull();
            }
        });
    }

    [Test]
    public async Task Another_principals_session_or_an_unknown_one_is_the_invalid_session_and_gets_no_window()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);
            var (id, session) = await ActiveOwnerAsync(host);

            // Act
            var stranger = await host.StepUpAsync(Guid.CreateVersion7(), session.Id, OwnersHost.Password);
            var missing = await host.StepUpAsync(id, Guid.CreateVersion7(), OwnersHost.Password);

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(stranger.Errors.Single()).IsTypeOf<UnauthenticatedError>();
                await Assert.That(missing.Errors.Single()).IsTypeOf<UnauthenticatedError>();
                await Assert.That((await host.CurrentAsync(session.Id)).Value.StepUpValidUntil).IsNull();
            }
        });
    }
}
