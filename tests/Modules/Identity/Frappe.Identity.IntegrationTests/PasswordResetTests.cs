using Frappe.Identity.Application.Codes;
using Frappe.Identity.Application.Sessions;
using Frappe.Identity.Domain;
using Frappe.Identity.Domain.Codes;
using Frappe.Results;
using Frappe.Testing.Containers;
using Microsoft.EntityFrameworkCore;

namespace Frappe.Identity.IntegrationTests;

/// <summary>
/// Resetting a forgotten password on a real Postgres (S11): a code goes only to an active owner, every other address is answered a made-up id
/// that cannot be told from a real one, the confirmation revokes every session, and guesses are bounded per code, per principal and by a limiter
/// that fails closed for real and made-up ids alike (SEC-06, SEC-14).
/// </summary>
[ClassDataSource<PostgresServer>(Shared = SharedType.PerTestSession)]
[NotInParallel]
public sealed class PasswordResetTests(PostgresServer server)
{
    private const string NewPassword = "a brand new passphrase";
    private const string Issued = "frappe.identity.owner_code_issued.v1";

    private static async Task<(Guid Id, OpenedSession Session)> ActiveOwnerAsync(OwnersHost host, string email = "ana@example.com")
    {
        var registered = await host.RegisterAsync(email);
        var verified = await host.VerifyAsync(registered.Value.Id.ToString(), await host.LastCodeAsync());
        await Assert.That(verified.IsSuccess).IsTrue();
        return (registered.Value.Id, verified.Value);
    }

    private static string WrongCodeFor(string code) => code == "000000" ? "000001" : "000000";

    [Test]
    public async Task An_active_owner_is_sent_a_code_whose_id_is_the_reset_id()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);
            var (id, _) = await ActiveOwnerAsync(host);
            using var counts = new Counts();

            // Act
            var requested = await host.RequestResetAsync(" Ana@Example.com ");

            // Assert
            await using var db = host.Context();
            var code = await db.OneTimeCodes.AsNoTracking().SingleAsync(c => c.Purpose == CodePurpose.PasswordReset);
            var mail = (await host.OutboxAsync()).Last(e => e.Type == Issued).Data;
            using (Assert.Multiple())
            {
                await Assert.That(requested.IsSuccess).IsTrue();
                await Assert.That(requested.Value.Id).IsEqualTo(code.Id);
                await Assert.That(code.PrincipalId).IsEqualTo(id);
                await Assert.That(mail.GetProperty("purpose").GetString()).IsEqualTo("password_reset");
                await Assert.That(mail.GetProperty("email").GetString()).IsEqualTo("ana@example.com");
                await Assert.That(counts.Of("frappe.identity.codes.issued", "password_reset")).IsEqualTo(1);
            }
        });
    }

    [Test]
    public async Task An_unknown_a_pending_and_a_malformed_address_are_answered_a_made_up_v7_id_and_write_nothing()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);
            await ActiveOwnerAsync(host);
            await host.RegisterAsync("pending@example.com");
            var before = (await host.OutboxAsync()).Count;
            var real = await host.RequestResetAsync("ana@example.com");

            // Act
            var unknown = await host.RequestResetAsync("nobody@example.com");
            var pending = await host.RequestResetAsync("pending@example.com");
            var malformed = await host.RequestResetAsync("not an address");
            var missing = await host.RequestResetAsync(null);

            // Assert
            await using var db = host.Context();
            var codes = await db.OneTimeCodes.AsNoTracking().Where(c => c.Purpose == CodePurpose.PasswordReset).ToListAsync();
            var mails = (await host.OutboxAsync()).Count(e => e.Type == Issued && e.Data.GetProperty("purpose").GetString() == "password_reset");
            using (Assert.Multiple())
            {
                foreach (var answer in new[] { real, unknown, pending, malformed, missing })
                {
                    await Assert.That(answer.IsSuccess).IsTrue();
                    await Assert.That(answer.Value.Id.Version).IsEqualTo(7);
                }

                await Assert.That(codes.Select(c => c.Id)).IsEquivalentTo([real.Value.Id]);
                await Assert.That(mails).IsEqualTo(1);
                await Assert.That((await host.OutboxAsync()).Count).IsGreaterThan(before);
                await Assert.That(new[] { unknown, pending, malformed, missing }.Select(a => a.Value.Id).Distinct().Count()).IsEqualTo(4);
            }
        });
    }

    [Test]
    public async Task The_send_budget_spent_answers_a_made_up_id_not_the_id_of_the_code_the_owner_holds()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);
            await ActiveOwnerAsync(host);
            var last = Guid.Empty;
            for (var i = 0; i < CodeLimits.Send.Limit; i++)
            {
                last = (await host.RequestResetAsync("ana@example.com")).Value.Id;
            }

            // Act
            var refused = await host.RequestResetAsync("ana@example.com");

            // Assert
            await using var db = host.Context();
            var live = await db.OneTimeCodes.AsNoTracking().SingleAsync(c => c.Purpose == CodePurpose.PasswordReset && c.InvalidatedAt == null);
            using (Assert.Multiple())
            {
                await Assert.That(refused.IsSuccess).IsTrue();
                await Assert.That(refused.Value.Id).IsNotEqualTo(live.Id);
                await Assert.That(live.Id).IsEqualTo(last);
                await Assert.That(refused.Value.Id.Version).IsEqualTo(7);
            }
        });
    }

    [Test]
    public async Task The_right_code_sets_the_password_and_revokes_every_session_with_the_reason_and_its_facts()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);
            var (_, first) = await ActiveOwnerAsync(host);
            var second = (await host.LogInAsync("ana@example.com", OwnersHost.Password)).Value.Session!;
            var requested = await host.RequestResetAsync("ana@example.com");
            var code = await host.LastCodeAsync();
            using var counts = new Counts();

            // Act
            var reset = await host.ResetPasswordAsync(requested.Value.Id.ToString(), code, NewPassword);

            // Assert
            await using var db = host.Context();
            var stored = await db.OneTimeCodes.AsNoTracking().SingleAsync(c => c.Id == requested.Value.Id);
            var events = (await host.OutboxAsync()).Select(e => e.Type).ToList();
            using (Assert.Multiple())
            {
                await Assert.That(reset.IsSuccess).IsTrue();
                await Assert.That(await host.LetsInAsync(first)).IsFalse();
                await Assert.That(await host.LetsInAsync(second)).IsFalse();
                await Assert.That(stored.RedeemedAt).IsNotNull();
                await Assert.That(events.Count(e => e == "frappe.identity.session_revoked.v1")).IsEqualTo(2);
                await Assert.That(events.Count(e => e == "frappe.identity.password_reset.v1")).IsEqualTo(1);
                await Assert.That(counts.Of("frappe.identity.password_changes", "reset")).IsEqualTo(1);
                await Assert.That(counts.Of("frappe.identity.sessions.revoked", "password_reset")).IsEqualTo(2);
                await Assert.That((await host.LogInAsync("ana@example.com", NewPassword)).IsSuccess).IsTrue();
                await Assert.That((await host.LogInAsync("ana@example.com", OwnersHost.Password)).IsSuccess).IsFalse();
                var again = await host.ResetPasswordAsync(requested.Value.Id.ToString(), code, "yet another passphrase");
                await Assert.That(again.Errors.Single().Code).IsEqualTo(IdentityErrorCodes.CodeInvalid);
            }
        });
    }

    [Test]
    public async Task A_wrong_code_a_made_up_id_and_a_malformed_id_answer_the_same_and_change_nothing()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);
            var (_, session) = await ActiveOwnerAsync(host);
            var requested = await host.RequestResetAsync("ana@example.com");
            var code = await host.LastCodeAsync();
            var decoy = await host.RequestResetAsync("nobody@example.com");

            // Act
            var wrong = await host.ResetPasswordAsync(requested.Value.Id.ToString(), WrongCodeFor(code), NewPassword);
            var madeUp = await host.ResetPasswordAsync(decoy.Value.Id.ToString(), code, NewPassword);
            var malformed = await host.ResetPasswordAsync("junk", code, NewPassword);
            var missing = await host.ResetPasswordAsync(null, null, NewPassword);

            // Assert
            using (Assert.Multiple())
            {
                foreach (var error in new[] { wrong, madeUp, malformed, missing }.Select(answer => answer.Errors.Single()))
                {
                    await Assert.That(error).IsTypeOf<RuleViolationError>();
                    await Assert.That(error.Code).IsEqualTo(IdentityErrorCodes.CodeInvalid);
                }

                await Assert.That(await host.LetsInAsync(session)).IsTrue();
                await Assert.That((await host.LogInAsync("ana@example.com", OwnersHost.Password)).IsSuccess).IsTrue();
            }
        });
    }

    [Test]
    public async Task A_down_limiter_answers_503_for_a_real_a_made_up_and_a_malformed_id_alike_and_judges_none()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);
            await ActiveOwnerAsync(host);
            var requested = await host.RequestResetAsync("ana@example.com");
            var code = await host.LastCodeAsync();
            var decoy = await host.RequestResetAsync("nobody@example.com");
            host.Limiter.Down = true;

            // Act
            var real = await host.ResetPasswordAsync(requested.Value.Id.ToString(), code, NewPassword);
            var madeUp = await host.ResetPasswordAsync(decoy.Value.Id.ToString(), code, NewPassword);
            var malformed = await host.ResetPasswordAsync("junk", code, NewPassword);

            // Assert
            host.Limiter.Down = false;
            await using var db = host.Context();
            var stored = await db.OneTimeCodes.AsNoTracking().SingleAsync(c => c.Id == requested.Value.Id);
            using (Assert.Multiple())
            {
                foreach (var error in new[] { real, madeUp, malformed }.Select(answer => answer.Errors.Single()))
                {
                    await Assert.That(error).IsTypeOf<UnavailableError>();
                    await Assert.That(error.Code).IsEqualTo(IdentityErrorCodes.AttemptsUnavailable);
                }

                await Assert.That(stored.RedeemedAt).IsNull();
            }
        });
    }

    [Test]
    public async Task A_weak_new_password_is_refused_without_costing_the_code_a_guess()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);
            await ActiveOwnerAsync(host);
            var requested = await host.RequestResetAsync("ana@example.com");
            var code = await host.LastCodeAsync();
            host.Breached.Add(NewPassword);

            // Act
            var breached = await host.ResetPasswordAsync(requested.Value.Id.ToString(), code, NewPassword);
            var tooShort = await host.ResetPasswordAsync(requested.Value.Id.ToString(), code, "short");
            var good = await host.ResetPasswordAsync(requested.Value.Id.ToString(), code, "a passphrase nobody leaked");

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(breached.Errors.Single().Code).IsEqualTo(IdentityErrorCodes.PasswordBreached);
                await Assert.That(tooShort.Errors.Single().Code).IsEqualTo(IdentityErrorCodes.PasswordTooShort);
                await Assert.That(good.IsSuccess).IsTrue();
                await Assert.That(host.Limiter.HitsOf(CodeLimits.Guess.Name, requested.Value.Id.ToString("N"))).IsEqualTo(1);
            }
        });
    }

    [Test]
    public async Task Asking_for_new_codes_never_buys_more_than_ten_guesses_a_day_per_principal()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);
            var (id, _) = await ActiveOwnerAsync(host);
            var guesses = 0;
            string? code = null;
            var last = Guid.Empty;
            for (var round = 0; round < 3 && guesses < CodeLimits.GuessPerPrincipal.Limit; round++)
            {
                last = (await host.RequestResetAsync("ana@example.com")).Value.Id;
                code = await host.LastCodeAsync();
                for (var i = 0; i < 4 && guesses < CodeLimits.GuessPerPrincipal.Limit; i++, guesses++)
                {
                    await host.ResetPasswordAsync(last.ToString(), WrongCodeFor(code), NewPassword);
                }
            }

            // Act: the ten guesses are spent, so the right code of a fresh request is refused too.
            var fresh = (await host.RequestResetAsync("ana@example.com")).Value.Id;
            var right = await host.ResetPasswordAsync(fresh.ToString(), await host.LastCodeAsync(), NewPassword);

            // Assert
            await using var db = host.Context();
            var stored = await db.OneTimeCodes.AsNoTracking().SingleAsync(c => c.Id == fresh);
            using (Assert.Multiple())
            {
                await Assert.That(guesses).IsEqualTo(CodeLimits.GuessPerPrincipal.Limit);
                await Assert.That(right.Errors.Single().Code).IsEqualTo(IdentityErrorCodes.CodeInvalid);
                await Assert.That(stored.RedeemedAt).IsNull();
                await Assert.That(host.Limiter.HitsOf(CodeLimits.GuessPerPrincipal.Name, $"PasswordReset:{id:N}:203.0.113.0/24"))
                    .IsEqualTo(CodeLimits.GuessPerPrincipal.Limit + 1);
            }
        });
    }

    [Test]
    public async Task One_network_exhausting_its_guesses_does_not_block_the_owner_from_another_network()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);
            var (id, _) = await ActiveOwnerAsync(host);
            for (var round = 0; round < 3; round++)
            {
                var attacked = (await host.RequestResetAsync("ana@example.com")).Value.Id;
                var real = await host.LastCodeAsync();
                for (var i = 0; i < 4; i++)
                {
                    await host.ResetPasswordAsync(attacked.ToString(), WrongCodeFor(real), NewPassword, "198.51.100.9");
                }
            }

            // Act
            var fresh = (await host.RequestResetAsync("ana@example.com")).Value.Id;
            var code = await host.LastCodeAsync();
            var blocked = await host.ResetPasswordAsync(fresh.ToString(), code, NewPassword, "198.51.100.9");
            var owners = await host.ResetPasswordAsync(fresh.ToString(), code, NewPassword, "192.0.2.50");

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(blocked.Errors.Single().Code).IsEqualTo(IdentityErrorCodes.CodeInvalid);
                await Assert.That(owners.IsSuccess).IsTrue();
                await Assert.That(host.Limiter.HitsOf(CodeLimits.GuessPerPrincipalAnywhere.Name, $"PasswordReset:{id:N}"))
                    .IsEqualTo(CodeLimits.GuessPerPrincipal.Limit + 1);
            }
        });
    }

    [Test]
    public async Task Five_wrong_guesses_kill_the_code_even_for_the_right_text()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);
            await ActiveOwnerAsync(host);
            var requested = await host.RequestResetAsync("ana@example.com");
            var code = await host.LastCodeAsync();
            for (var i = 0; i < CodeLimits.Guess.Limit; i++)
            {
                await host.ResetPasswordAsync(requested.Value.Id.ToString(), WrongCodeFor(code), NewPassword);
            }

            // Act
            var right = await host.ResetPasswordAsync(requested.Value.Id.ToString(), code, NewPassword);

            // Assert
            await Assert.That(right.Errors.Single().Code).IsEqualTo(IdentityErrorCodes.CodeInvalid);
        });
    }

    [Test]
    public async Task A_newer_request_supersedes_the_older_id_even_with_the_text_of_the_newer_code()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);
            await ActiveOwnerAsync(host);
            var older = await host.RequestResetAsync("ana@example.com");
            var newer = await host.RequestResetAsync("ana@example.com");
            var newerCode = await host.LastCodeAsync();

            // Act
            var withOlderId = await host.ResetPasswordAsync(older.Value.Id.ToString(), newerCode, NewPassword);
            var withNewerId = await host.ResetPasswordAsync(newer.Value.Id.ToString(), newerCode, NewPassword);

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(withOlderId.Errors.Single().Code).IsEqualTo(IdentityErrorCodes.CodeInvalid);
                await Assert.That(withNewerId.IsSuccess).IsTrue();
            }
        });
    }

    [Test]
    public async Task A_code_sent_to_an_address_the_owner_no_longer_has_resets_nothing()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);
            var (id, _) = await ActiveOwnerAsync(host);
            var requested = await host.RequestResetAsync("ana@example.com");
            var code = await host.LastCodeAsync();
            await using (var db = host.Context())
            {
                await db.Database.ExecuteSqlAsync($"""
                    UPDATE identity.email_identifiers SET address = 'new@example.com', normalized_address = 'new@example.com'
                    WHERE principal_id = {id}
                    """);
            }

            // Act
            var reset = await host.ResetPasswordAsync(requested.Value.Id.ToString(), code, NewPassword);

            // Assert
            await Assert.That(reset.Errors.Single().Code).IsEqualTo(IdentityErrorCodes.CodeInvalid);
        });
    }

    [Test]
    public async Task Two_requests_for_the_same_address_at_once_both_answer_and_leave_one_live_code()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using (var setup = OwnersHost.Over(owner))
            {
                await ActiveOwnerAsync(setup);
            }

            await using var host = OwnersHost.Over(owner, new OwnersHost.RaceGate(2));

            // Act
            var answers = await Task.WhenAll(host.RequestResetAsync("ana@example.com"), host.RequestResetAsync("ana@example.com"));

            // Assert
            await using var db = host.Context();
            var live = await db.OneTimeCodes.AsNoTracking()
                .Where(c => c.Purpose == CodePurpose.PasswordReset && c.RedeemedAt == null && c.InvalidatedAt == null).ToListAsync();
            using (Assert.Multiple())
            {
                await Assert.That(answers.All(a => a.IsSuccess)).IsTrue();
                await Assert.That(live.Count).IsEqualTo(1);
            }
        });
    }
}
