using Frappe.Identity.Application.Owners.LogInOwner;
using Frappe.Identity.Domain;
using Frappe.Identity.Domain.Principals;
using Frappe.Results;
using Frappe.Testing.Containers;
using Microsoft.EntityFrameworkCore;

namespace Frappe.Identity.IntegrationTests;

/// <summary>
/// Logging an owner in on a real Postgres (S7): the right password opens a session in one commit, a pending owner with the right password is
/// sent a new code that commits and is told to verify, every other case is the one 401 never faster than 500 ms, and an address that tries too
/// often is throttled (SEC-06, SEC-14).
/// </summary>
[ClassDataSource<PostgresServer>(Shared = SharedType.PerTestSession)]
[NotInParallel]
public sealed class OwnerLogInTests(PostgresServer server)
{
    private const string CodeIssued = "frappe.identity.owner_code_issued.v1";

    private static readonly Error Invalid = new UnauthenticatedError(IdentityErrorCodes.CredentialsInvalid);

    private static async Task<Guid> ActiveOwnerAsync(OwnersHost host, string email = "ana@example.com")
    {
        var registered = await host.RegisterAsync(email);
        var verified = await host.VerifyAsync(registered.Value.Id.ToString(), await host.LastCodeAsync());
        await Assert.That(verified.IsSuccess).IsTrue();
        return registered.Value.Id;
    }

    [Test]
    public async Task The_right_password_opens_a_session_in_one_commit_and_counts_it()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);
            var id = await ActiveOwnerAsync(host);
            using var counts = new Counts();

            // Act
            var result = await host.LogInAsync("ana@example.com", OwnersHost.Password);

            // Assert
            await using var db = host.Context();
            var sessions = await db.Sessions.AsNoTracking().OrderBy(s => s.OpenedAt).ToListAsync();
            var events = (await host.OutboxAsync()).Select(e => e.Type).ToList();
            using (Assert.Multiple())
            {
                await Assert.That(result.IsSuccess).IsTrue();
                await Assert.That(result.Value.PendingOwnerId).IsNull();
                await Assert.That(sessions.Count).IsEqualTo(2);
                await Assert.That(result.Value.Session!.Id).IsEqualTo(sessions[1].Id);
                await Assert.That(result.Value.Session.Token.Value).StartsWith("fs_");
                await Assert.That(sessions[1].PrincipalId).IsEqualTo(id);
                await Assert.That(sessions[1].IpPrefix!.ToString()).IsEqualTo("203.0.113.0/24");
                await Assert.That(events.Count(e => e == "frappe.identity.session_opened.v1")).IsEqualTo(2);
                await Assert.That(counts.Of("frappe.identity.logins", "opened")).IsEqualTo(1);
                await Assert.That(counts.Of("frappe.identity.sessions.opened", "console")).IsEqualTo(1);
            }
        });
    }

    [Test]
    public async Task The_right_password_answers_at_once_without_waiting_for_the_floor()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);
            await ActiveOwnerAsync(host);

            // Act
            var result = await host.StartLogInAsync("ana@example.com", OwnersHost.Password);

            // Assert
            await Assert.That(result.IsSuccess).IsTrue();
        });
    }

    [Test]
    [Arguments("  ANA@Example.COM ")]
    [Arguments("ana@example.com")]
    public async Task The_address_is_matched_trimmed_and_without_regard_to_case(string typed)
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);
            await ActiveOwnerAsync(host);

            // Act
            var result = await host.StartLogInAsync(typed, OwnersHost.Password);

            // Assert
            await Assert.That(result.IsSuccess).IsTrue();
        });
    }

    [Test]
    [Arguments("ana@example.com", "wrong password here")]
    [Arguments("nobody@example.com", OwnersHost.Password)]
    [Arguments("not an address", OwnersHost.Password)]
    [Arguments(null, OwnersHost.Password)]
    [Arguments("ana@example.com", null)]
    [Arguments("ana@example.com", "")]
    public async Task Every_refusal_is_the_one_401_and_is_never_faster_than_the_floor(string? email, string? password)
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);
            await ActiveOwnerAsync(host);
            using var counts = new Counts();

            // Act
            var before = host.Time.GetUtcNow();
            var result = await host.LogInAsync(email, password);

            // Assert
            await using var db = host.Context();
            using (Assert.Multiple())
            {
                await Assert.That(host.Time.GetUtcNow() - before).IsGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(500));
                await Assert.That(result.IsSuccess).IsFalse();
                await Assert.That(result.Errors.Single()).IsEqualTo(Invalid);
                await Assert.That(await db.Sessions.CountAsync()).IsEqualTo(1);
                await Assert.That(counts.Of("frappe.identity.logins", "invalid")).IsEqualTo(1);
            }
        });
    }

    [Test]
    public async Task A_pending_owner_with_the_right_password_is_sent_a_new_code_that_commits_and_told_to_verify()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);
            var registered = await host.RegisterAsync();
            var old = await host.LastCodeAsync();
            using var counts = new Counts();

            // Act
            var result = await host.LogInAsync("ana@example.com", OwnersHost.Password);

            // Assert
            var fresh = await host.LastCodeAsync();
            var withOld = await host.VerifyAsync(registered.Value.Id.ToString(), old == fresh ? "000000" : old);
            var withNew = await host.VerifyAsync(registered.Value.Id.ToString(), fresh);
            await using var db = host.Context();
            using (Assert.Multiple())
            {
                await Assert.That(result.IsSuccess).IsTrue();
                await Assert.That(result.Value.PendingOwnerId).IsEqualTo(registered.Value.Id);
                await Assert.That(result.Value.Session).IsNull();
                await Assert.That((await host.OutboxAsync()).Count(e => e.Type == CodeIssued)).IsEqualTo(2);
                await Assert.That(withOld.IsSuccess).IsFalse();
                await Assert.That(withNew.IsSuccess).IsTrue();
                await Assert.That(counts.Of("frappe.identity.logins", "pending")).IsEqualTo(1);
            }
        });
    }

    [Test]
    public async Task A_pending_owner_with_the_right_password_opens_no_session()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);
            await host.RegisterAsync();

            // Act
            await host.LogInAsync("ana@example.com", OwnersHost.Password);

            // Assert
            await using var db = host.Context();
            using (Assert.Multiple())
            {
                await Assert.That(await db.Sessions.CountAsync()).IsEqualTo(0);
                await Assert.That((await db.Principals.AsNoTracking().SingleAsync()).Status).IsEqualTo(PrincipalStatus.Pending);
            }
        });
    }

    [Test]
    public async Task A_pending_owner_with_a_wrong_password_is_refused_and_no_code_is_sent()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);
            await host.RegisterAsync();

            // Act
            var result = await host.LogInAsync("ana@example.com", "wrong password here");

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(result.Errors.Single()).IsEqualTo(Invalid);
                await Assert.That((await host.OutboxAsync()).Count(e => e.Type == CodeIssued)).IsEqualTo(1);
            }
        });
    }

    [Test]
    public async Task A_pending_owner_over_the_send_budget_is_still_told_to_verify_without_a_new_code()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);
            var registered = await host.RegisterAsync();
            for (var i = 0; i < 4; i++)
            {
                await host.ResendAsync(registered.Value.Id.ToString());
            }

            // Act
            var result = await host.LogInAsync("ana@example.com", OwnersHost.Password);

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(result.IsSuccess).IsTrue();
                await Assert.That(result.Value.PendingOwnerId).IsEqualTo(registered.Value.Id);
                await Assert.That((await host.OutboxAsync()).Count(e => e.Type == CodeIssued)).IsEqualTo(5);
            }
        });
    }

    [Test]
    public async Task An_address_that_tries_too_often_is_throttled_with_the_wait_whatever_it_is_and_the_others_are_not()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);
            await ActiveOwnerAsync(host);
            using var counts = new Counts();
            for (var i = 0; i < LogInLimits.Attempt.Limit; i++)
            {
                await host.StartLogInAsync("ana@example.com", OwnersHost.Password);
            }

            // Act
            var throttled = await host.LogInAsync("ANA@example.com", OwnersHost.Password);
            var unknownTooOften = new List<Result<LogInOutcome>>();
            for (var i = 0; i <= LogInLimits.Attempt.Limit; i++)
            {
                unknownTooOften.Add(await host.LogInAsync("nobody@example.com", "whatever password"));
            }

            var another = await host.LogInAsync("bob@example.com", OwnersHost.Password);

            // Assert
            var error = (RateLimitedError)throttled.Errors.Single();
            var unknownError = (RateLimitedError)unknownTooOften[^1].Errors.Single();
            using (Assert.Multiple())
            {
                await Assert.That(error.Code).IsEqualTo("http.rate_limited");
                await Assert.That(error.RetryAfterSeconds).IsEqualTo((int)LogInLimits.Attempt.Window.TotalSeconds);
                await Assert.That(unknownError.Code).IsEqualTo("http.rate_limited");
                await Assert.That(unknownTooOften.Take(LogInLimits.Attempt.Limit).All(r => r.Errors.Single() == Invalid)).IsTrue();
                await Assert.That(another.Errors.Single()).IsEqualTo(Invalid);
                await Assert.That(counts.Of("frappe.identity.logins", "throttled")).IsEqualTo(2);
            }
        });
    }

    [Test]
    public async Task One_network_spending_its_budget_does_not_throttle_the_same_address_from_another_network()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);
            await ActiveOwnerAsync(host);
            for (var i = 0; i < LogInLimits.Attempt.Limit; i++)
            {
                await host.StartLogInAsync("ana@example.com", OwnersHost.Password, "203.0.113.7");
            }

            // Act
            var sameNetwork = await host.LogInAsync("ana@example.com", OwnersHost.Password, "203.0.113.200");
            var otherNetwork = await host.LogInAsync("ana@example.com", OwnersHost.Password, "198.51.100.9");

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(sameNetwork.Errors.Single()).IsTypeOf<RateLimitedError>();
                await Assert.That(otherNetwork.IsSuccess).IsTrue();
            }
        });
    }

    [Test]
    public async Task The_wide_cap_on_one_address_throttles_it_from_every_network()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);
            await ActiveOwnerAsync(host);
            for (var i = 0; i < LogInLimits.AttemptByEmail.Limit; i++)
            {
                await host.StartLogInAsync("ana@example.com", OwnersHost.Password, $"10.{i / 250}.{i % 250}.1");
            }

            // Act
            var throttled = await host.LogInAsync("ana@example.com", OwnersHost.Password, "198.51.100.9");

            // Assert
            var error = (RateLimitedError)throttled.Errors.Single();
            using (Assert.Multiple())
            {
                await Assert.That(error.Code).IsEqualTo("http.rate_limited");
                await Assert.That(error.RetryAfterSeconds).IsGreaterThan(0);
            }
        });
    }

    [Test]
    public async Task While_attempts_cannot_be_counted_none_is_judged()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);
            await ActiveOwnerAsync(host);
            host.Limiter.Down = true;

            // Act
            var result = await host.StartLogInAsync("ana@example.com", OwnersHost.Password);

            // Assert
            await using var db = host.Context();
            using (Assert.Multiple())
            {
                await Assert.That(result.Errors.Single()).IsEqualTo(new UnavailableError(IdentityErrorCodes.AttemptsUnavailable));
                await Assert.That(await db.Sessions.CountAsync()).IsEqualTo(1);
            }
        });
    }
}
