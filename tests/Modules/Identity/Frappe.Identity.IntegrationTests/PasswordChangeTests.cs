using Frappe.Identity.Application.Credentials;
using Frappe.Identity.Application.Sessions;
using Frappe.Identity.Domain;
using Frappe.Identity.Domain.Sessions;
using Frappe.Results;
using Frappe.Testing.Containers;
using Microsoft.EntityFrameworkCore;

namespace Frappe.Identity.IntegrationTests;

/// <summary>
/// Changing a password while signed in, on a real Postgres (S12): every other session stops at once, the one that asked keeps working, the
/// current password is rechecked under its own throttle, and a log-in racing the change never leaves a session behind (S2).
/// </summary>
[ClassDataSource<PostgresServer>(Shared = SharedType.PerTestSession)]
[NotInParallel]
public sealed class PasswordChangeTests(PostgresServer server)
{
    private const string NewPassword = "a brand new passphrase";

    private static async Task<(Guid Id, OpenedSession Session)> ActiveOwnerAsync(OwnersHost host)
    {
        var registered = await host.RegisterAsync();
        var verified = await host.VerifyAsync(registered.Value.Id.ToString(), await host.LastCodeAsync());
        await Assert.That(verified.IsSuccess).IsTrue();
        return (registered.Value.Id, verified.Value);
    }

    [Test]
    public async Task Other_sessions_are_rejected_at_once_the_current_one_still_works_and_the_new_password_logs_in()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);
            var (id, current) = await ActiveOwnerAsync(host);
            var other = (await host.LogInAsync("ana@example.com", OwnersHost.Password)).Value.Session!;
            var another = (await host.LogInAsync("ana@example.com", OwnersHost.Password)).Value.Session!;
            using var counts = new Counts();

            // Act
            var changed = await host.ChangePasswordAsync(id, current.Id, OwnersHost.Password, NewPassword);

            // Assert
            await using var db = host.Context();
            var revoked = await db.Sessions.AsNoTracking().Where(s => s.RevokedAt != null).ToListAsync();
            var events = (await host.OutboxAsync()).Select(e => e.Type).ToList();
            using (Assert.Multiple())
            {
                await Assert.That(changed.IsSuccess).IsTrue();
                await Assert.That(await host.LetsInAsync(current)).IsTrue();
                await Assert.That(await host.LetsInAsync(other)).IsFalse();
                await Assert.That(await host.LetsInAsync(another)).IsFalse();
                await Assert.That(revoked.Select(s => s.Id)).IsEquivalentTo([other.Id, another.Id]);
                await Assert.That(revoked.All(s => s.RevocationReason == RevocationReason.PasswordChanged)).IsTrue();
                await Assert.That(events.Count(e => e == "frappe.identity.session_revoked.v1")).IsEqualTo(2);
                await Assert.That(events.Count(e => e == "frappe.identity.password_changed.v1")).IsEqualTo(1);
                await Assert.That(counts.Of("frappe.identity.password_changes", "change")).IsEqualTo(1);
                await Assert.That(counts.Of("frappe.identity.sessions.revoked", "password_changed")).IsEqualTo(2);
                await Assert.That((await host.LogInAsync("ana@example.com", NewPassword)).IsSuccess).IsTrue();
                await Assert.That((await host.LogInAsync("ana@example.com", OwnersHost.Password)).IsSuccess).IsFalse();
            }
        });
    }

    [Test]
    public async Task A_wrong_current_password_changes_nothing_and_a_weak_new_one_changes_nothing()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);
            var (id, current) = await ActiveOwnerAsync(host);
            var other = (await host.LogInAsync("ana@example.com", OwnersHost.Password)).Value.Session!;
            host.Breached.Add(NewPassword);

            // Act
            var wrong = await host.ChangePasswordAsync(id, current.Id, "not my password!", "another fine passphrase");
            var breached = await host.ChangePasswordAsync(id, current.Id, OwnersHost.Password, NewPassword);
            var tooShort = await host.ChangePasswordAsync(id, current.Id, OwnersHost.Password, "short");

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(wrong.Errors.Single().Code).IsEqualTo(IdentityErrorCodes.PasswordIncorrect);
                await Assert.That(wrong.Errors.Single()).IsTypeOf<RuleViolationError>();
                await Assert.That(breached.Errors.Single().Code).IsEqualTo(IdentityErrorCodes.PasswordBreached);
                await Assert.That(tooShort.Errors.Single().Code).IsEqualTo(IdentityErrorCodes.PasswordTooShort);
                await Assert.That(await host.LetsInAsync(other)).IsTrue();
                await Assert.That((await host.LogInAsync("ana@example.com", OwnersHost.Password)).IsSuccess).IsTrue();
                await Assert.That((await host.OutboxAsync()).Any(e => e.Type == "frappe.identity.password_changed.v1")).IsFalse();
            }
        });
    }

    [Test]
    public async Task The_current_password_is_rechecked_ten_times_per_principal_then_throttled_even_when_right()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);
            var (id, current) = await ActiveOwnerAsync(host);
            for (var i = 0; i < PasswordRecheck.Flow.Limit; i++)
            {
                await host.ChangePasswordAsync(id, current.Id, "not my password!", NewPassword);
            }

            // Act
            var throttled = await host.ChangePasswordAsync(id, current.Id, OwnersHost.Password, NewPassword);

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(throttled.Errors.Single()).IsTypeOf<RateLimitedError>();
                await Assert.That(host.Limiter.HitsOf(PasswordRecheck.Flow.Name, id.ToString("N"))).IsEqualTo(PasswordRecheck.Flow.Limit + 1);
                await Assert.That((await host.OutboxAsync()).Any(e => e.Type == "frappe.identity.password_changed.v1")).IsFalse();
            }
        });
    }

    [Test]
    public async Task While_rechecks_cannot_be_counted_no_change_is_judged()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);
            var (id, current) = await ActiveOwnerAsync(host);
            host.Limiter.Down = true;

            // Act
            var result = await host.ChangePasswordAsync(id, current.Id, OwnersHost.Password, NewPassword);

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(result.Errors.Single()).IsTypeOf<UnavailableError>();
                await Assert.That(result.Errors.Single().Code).IsEqualTo(IdentityErrorCodes.AttemptsUnavailable);
            }
        });
    }

    [Test]
    public async Task A_log_in_racing_the_change_leaves_no_session_that_the_old_password_opened()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);
            var (id, current) = await ActiveOwnerAsync(host);

            // Act: both start together; the log-in may win or lose the race, either way the invariant below holds.
            var login = host.StartLogInAsync("ana@example.com", OwnersHost.Password);
            var change = host.ChangePasswordAsync(id, current.Id, OwnersHost.Password, NewPassword);
            await Task.WhenAll(login, change);
            var changed = await change;

            // Assert
            await using var db = host.Context();
            var sessions = await db.Sessions.AsNoTracking().Where(s => s.PrincipalId == id).ToListAsync();
            var principal = await db.Principals.AsNoTracking().SingleAsync(p => p.Id == id);
            var now = NodaTime.Instant.FromDateTimeOffset(host.Time.GetUtcNow());
            var alive = sessions.Where(s => s.IsActiveFor(now, principal)).Select(s => s.Id).ToList();
            using (Assert.Multiple())
            {
                await Assert.That(changed.IsSuccess).IsTrue();
                await Assert.That(alive).IsEquivalentTo([current.Id]);
            }
        });
    }
}
