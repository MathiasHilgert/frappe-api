using Frappe.Identity.Application.Codes;
using Frappe.Identity.Application.Sessions;
using Frappe.Identity.Domain;
using Frappe.Identity.Domain.Codes;
using Frappe.Identity.Domain.Sessions;
using Frappe.Testing.Containers;
using Microsoft.EntityFrameworkCore;

namespace Frappe.Identity.IntegrationTests;

/// <summary>
/// Changing an owner's address on a real Postgres (S15): a code goes to the new address and the old one is told, a taken address answers like a
/// free one and does everything but mail the code, the confirmation replaces the address, deletes a pending squatter, revokes the other sessions
/// and invalidates every live code of the owner.
/// </summary>
[ClassDataSource<PostgresServer>(Shared = SharedType.PerTestSession)]
[NotInParallel]
public sealed class EmailChangeTests(PostgresServer server)
{
    private const string Issued = "frappe.identity.owner_code_issued.v1";
    private const string Requested = "frappe.identity.owner_email_change_requested.v1";
    private const string Changed = "frappe.identity.owner_email_changed.v1";

    private static async Task<(Guid Id, OpenedSession Session)> ActiveOwnerAsync(OwnersHost host, string email = "ana@example.com")
    {
        var registered = await host.RegisterAsync(email);
        var verified = await host.VerifyAsync(registered.Value.Id.ToString(), await host.LastCodeAsync());
        await Assert.That(verified.IsSuccess).IsTrue();
        return (registered.Value.Id, verified.Value);
    }

    [Test]
    public async Task A_free_address_gets_the_code_the_old_one_gets_the_notice_and_the_old_one_keeps_working()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);
            var (id, _) = await ActiveOwnerAsync(host);

            // Act
            var requested = await host.RequestEmailChangeAsync(id, " New@Example.com ");

            // Assert
            await using var db = host.Context();
            var code = await db.OneTimeCodes.AsNoTracking().SingleAsync(c => c.Purpose == CodePurpose.EmailChange);
            var events = await host.OutboxAsync();
            var mail = events.Last(e => e.Type == Issued).Data;
            var notice = events.Single(e => e.Type == Requested).Data;
            using (Assert.Multiple())
            {
                await Assert.That(requested.IsSuccess).IsTrue();
                await Assert.That(requested.Value.Id).IsEqualTo(code.Id);
                await Assert.That(code.Target).IsEqualTo("new@example.com");
                await Assert.That(mail.GetProperty("purpose").GetString()).IsEqualTo("email_change");
                await Assert.That(mail.GetProperty("email").GetString()).IsEqualTo("New@Example.com");
                await Assert.That(notice.GetProperty("oldEmail").GetString()).IsEqualTo("ana@example.com");
                await Assert.That(notice.GetProperty("newEmailMasked").GetString()).IsEqualTo("***@Example.com");
                await Assert.That((await host.LogInAsync("ana@example.com", OwnersHost.Password)).IsSuccess).IsTrue();
                await Assert.That((await host.LogInAsync("new@example.com", OwnersHost.Password)).IsSuccess).IsFalse();
            }
        });
    }

    [Test]
    public async Task A_taken_address_answers_a_v7_id_and_only_skips_the_code_mail()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);
            var (id, _) = await ActiveOwnerAsync(host);
            await ActiveOwnerAsync(host, "bob@example.com");
            var previous = await host.RequestEmailChangeAsync(id, "free@example.com");
            var mailsBefore = (await host.OutboxAsync()).Count(e => e.Type == Issued && e.Data.GetProperty("purpose").GetString() == "email_change");

            // Act
            var taken = await host.RequestEmailChangeAsync(id, "bob@example.com");

            // Assert
            await using var db = host.Context();
            var codes = await db.OneTimeCodes.AsNoTracking().Where(c => c.Purpose == CodePurpose.EmailChange).ToListAsync();
            var events = await host.OutboxAsync();
            using (Assert.Multiple())
            {
                await Assert.That(taken.IsSuccess).IsTrue();
                await Assert.That(taken.Value.Id.Version).IsEqualTo(7);
                await Assert.That(codes.Select(c => c.Id)).IsEquivalentTo([previous.Value.Id]);
                await Assert.That(codes.Single().InvalidatedAt).IsNotNull();
                await Assert.That(events.Count(e => e.Type == Issued && e.Data.GetProperty("purpose").GetString() == "email_change"))
                    .IsEqualTo(mailsBefore);
                await Assert.That(events.Count(e => e.Type == Requested)).IsEqualTo(2);
                await Assert.That(host.Limiter.HitsOf(CodeLimits.Send.Name, $"EmailChange:{id:N}")).IsEqualTo(2);
            }
        });
    }

    [Test]
    public async Task The_code_replaces_the_address_revokes_the_other_sessions_and_invalidates_every_live_code()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);
            var (id, current) = await ActiveOwnerAsync(host);
            var other = (await host.LogInAsync("ana@example.com", OwnersHost.Password)).Value.Session!;
            var reset = await host.RequestResetAsync("ana@example.com");
            var requested = await host.RequestEmailChangeAsync(id, "new@example.com");
            var code = await host.LastCodeAsync();

            // Act
            var confirmed = await host.ConfirmEmailChangeAsync(id, current.Id, requested.Value.Id.ToString(), code);

            // Assert
            await using var db = host.Context();
            var identifier = await db.EmailIdentifiers.AsNoTracking().SingleAsync(e => e.PrincipalId == id);
            var live = await db.OneTimeCodes.AsNoTracking().Where(c => c.PrincipalId == id && c.RedeemedAt == null && c.InvalidatedAt == null)
                .ToListAsync();
            var revoked = await db.Sessions.AsNoTracking().Where(s => s.RevokedAt != null).ToListAsync();
            using (Assert.Multiple())
            {
                await Assert.That(confirmed.IsSuccess).IsTrue();
                await Assert.That(identifier.NormalizedAddress).IsEqualTo("new@example.com");
                await Assert.That(identifier.VerifiedAt).IsNotNull();
                await Assert.That(live).IsEmpty();
                await Assert.That(await host.LetsInAsync(current)).IsTrue();
                await Assert.That(await host.LetsInAsync(other)).IsFalse();
                await Assert.That(revoked.Select(s => s.Id)).IsEquivalentTo([other.Id]);
                await Assert.That(revoked.Single().RevocationReason).IsEqualTo(RevocationReason.EmailChanged);
                await Assert.That((await host.OutboxAsync()).Count(e => e.Type == Changed)).IsEqualTo(1);
                await Assert.That((await host.LogInAsync("new@example.com", OwnersHost.Password)).IsSuccess).IsTrue();
                await Assert.That((await host.LogInAsync("ana@example.com", OwnersHost.Password)).IsSuccess).IsFalse();
                var late = await host.ResetPasswordAsync(reset.Value.Id.ToString(), await ResetCodeAsync(host), "yet another passphrase");
                await Assert.That(late.Errors.Single().Code).IsEqualTo(IdentityErrorCodes.CodeInvalid);
            }
        });
    }

    [Test]
    public async Task A_pending_owner_holding_the_address_is_deleted_when_the_change_is_confirmed()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);
            var (id, current) = await ActiveOwnerAsync(host);
            var squatter = await host.RegisterAsync("squat@example.com");
            var requested = await host.RequestEmailChangeAsync(id, "squat@example.com");
            var code = await host.LastCodeAsync();

            // Act
            var confirmed = await host.ConfirmEmailChangeAsync(id, current.Id, requested.Value.Id.ToString(), code);

            // Assert
            await using var db = host.Context();
            using (Assert.Multiple())
            {
                await Assert.That(confirmed.IsSuccess).IsTrue();
                await Assert.That(await db.Principals.AnyAsync(p => p.Id == squatter.Value.Id)).IsFalse();
                await Assert.That(await db.Owners.AnyAsync(o => o.PrincipalId == squatter.Value.Id)).IsFalse();
                await Assert.That((await db.EmailIdentifiers.SingleAsync(e => e.PrincipalId == id)).NormalizedAddress).IsEqualTo("squat@example.com");
            }
        });
    }

    [Test]
    public async Task A_holder_that_verified_meanwhile_keeps_the_address_and_the_change_answers_code_invalid()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);
            var (id, current) = await ActiveOwnerAsync(host);
            var squatter = await host.RegisterAsync("squat@example.com");
            var requested = await host.RequestEmailChangeAsync(id, "squat@example.com");
            var code = await host.LastCodeAsync();
            await host.VerifyAsync(squatter.Value.Id.ToString(), await LastVerificationCodeAsync(host));

            // Act
            var confirmed = await host.ConfirmEmailChangeAsync(id, current.Id, requested.Value.Id.ToString(), code);

            // Assert
            await using var db = host.Context();
            using (Assert.Multiple())
            {
                await Assert.That(confirmed.Errors.Single().Code).IsEqualTo(IdentityErrorCodes.CodeInvalid);
                await Assert.That(await db.Principals.AnyAsync(p => p.Id == squatter.Value.Id)).IsTrue();
                await Assert.That((await db.EmailIdentifiers.SingleAsync(e => e.PrincipalId == id)).NormalizedAddress).IsEqualTo("ana@example.com");
                await Assert.That((await db.OneTimeCodes.SingleAsync(c => c.Id == requested.Value.Id)).RedeemedAt).IsNull();
            }
        });
    }

    [Test]
    public async Task A_wrong_code_a_made_up_id_and_another_owners_id_answer_code_invalid_and_change_nothing()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);
            var (id, current) = await ActiveOwnerAsync(host);
            var (otherId, otherSession) = await ActiveOwnerAsync(host, "bob@example.com");
            var requested = await host.RequestEmailChangeAsync(id, "new@example.com");
            var code = await host.LastCodeAsync();

            // Act
            var wrong = await host.ConfirmEmailChangeAsync(id, current.Id, requested.Value.Id.ToString(), code == "000000" ? "000001" : "000000");
            var madeUp = await host.ConfirmEmailChangeAsync(id, current.Id, Guid.CreateVersion7().ToString(), code);
            var malformed = await host.ConfirmEmailChangeAsync(id, current.Id, "junk", code);
            var foreign = await host.ConfirmEmailChangeAsync(otherId, otherSession.Id, requested.Value.Id.ToString(), code);

            // Assert
            using (Assert.Multiple())
            {
                foreach (var error in new[] { wrong, madeUp, malformed, foreign }.Select(answer => answer.Errors.Single()))
                {
                    await Assert.That(error.Code).IsEqualTo(IdentityErrorCodes.CodeInvalid);
                }

                await Assert.That((await host.LogInAsync("ana@example.com", OwnersHost.Password)).IsSuccess).IsTrue();
                var ok = await host.ConfirmEmailChangeAsync(id, current.Id, requested.Value.Id.ToString(), code);
                await Assert.That(ok.IsSuccess).IsTrue();
            }
        });
    }

    [Test]
    public async Task A_reset_code_for_an_address_the_owner_no_longer_has_is_refused()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);
            await ActiveOwnerAsync(host);
            var requested = await host.RequestResetAsync("ana@example.com");
            var code = await host.LastCodeAsync();
            await using (var db = host.Context())
            {
                await db.Database.ExecuteSqlRawAsync("UPDATE identity.email_identifiers SET normalized_address = 'moved@example.com'");
            }

            // Act
            var reset = await host.ResetPasswordAsync(requested.Value.Id.ToString(), code, "a brand new passphrase");

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(reset.Errors.Single().Code).IsEqualTo(IdentityErrorCodes.CodeInvalid);
                await Assert.That((await host.LogInAsync("ana@example.com", OwnersHost.Password)).IsSuccess).IsFalse();
            }
        });
    }

    private static async Task<string> ResetCodeAsync(OwnersHost host) =>
        (await host.OutboxAsync()).Last(e => e.Type == Issued && e.Data.GetProperty("purpose").GetString() == "password_reset")
            .Data.GetProperty("code").GetString()!;

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Two_requests_at_once_both_answer_whether_the_address_is_free_or_taken(bool taken)
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using (var setup = OwnersHost.Over(owner))
            {
                var (id, _) = await ActiveOwnerAsync(setup);
                await ActiveOwnerAsync(setup, "bob@example.com");
                if (taken)
                {
                    await setup.RequestEmailChangeAsync(id, "free@example.com");
                }
            }

            await using var host = OwnersHost.Over(owner, new OwnersHost.RaceGate(2));
            await using var db = host.Context();
            var principal = (await db.EmailIdentifiers.AsNoTracking().SingleAsync(i => i.NormalizedAddress == "ana@example.com")).PrincipalId;
            var target = taken ? "bob@example.com" : "new@example.com";

            // Act
            var answers = await Task.WhenAll(host.RequestEmailChangeAsync(principal, target), host.RequestEmailChangeAsync(principal, target));

            // Assert
            await Assert.That(answers.All(a => a.IsSuccess)).IsTrue();
        });
    }

    [Test]
    public async Task Two_owners_confirming_the_same_address_at_once_leave_one_holder_and_the_loser_is_told_the_code_is_invalid()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            Guid ana;
            Guid bob;
            (OpenedSession Session, string Change, string Code) anaRequest;
            (OpenedSession Session, string Change, string Code) bobRequest;
            await using (var seed = OwnersHost.Over(owner))
            {
                OpenedSession anaSession;
                OpenedSession bobSession;
                (ana, anaSession) = await ActiveOwnerAsync(seed);
                (bob, bobSession) = await ActiveOwnerAsync(seed, "bob@example.com");
                var anaChange = await seed.RequestEmailChangeAsync(ana, "new@example.com");
                anaRequest = (anaSession, anaChange.Value.Id.ToString(), await seed.LastCodeAsync());
                var bobChange = await seed.RequestEmailChangeAsync(bob, "new@example.com");
                bobRequest = (bobSession, bobChange.Value.Id.ToString(), await seed.LastCodeAsync());
            }

            await using var host = OwnersHost.Over(owner, saving: new OwnersHost.RaceGate(2));

            // Act
            var answers = await Task.WhenAll(
                host.ConfirmEmailChangeAsync(ana, anaRequest.Session.Id, anaRequest.Change, anaRequest.Code),
                host.ConfirmEmailChangeAsync(bob, bobRequest.Session.Id, bobRequest.Change, bobRequest.Code));

            // Assert
            await using var db = host.Context();
            using (Assert.Multiple())
            {
                await Assert.That(answers.Count(a => a.IsSuccess)).IsEqualTo(1);
                await Assert.That(answers.Single(a => !a.IsSuccess).Errors.Single().Code).IsEqualTo(IdentityErrorCodes.CodeInvalid);
                await Assert.That(await db.EmailIdentifiers.CountAsync(i => i.NormalizedAddress == "new@example.com")).IsEqualTo(1);
            }
        });
    }

    [Test]
    public async Task A_wrong_confirmation_spends_the_same_guesses_after_a_taken_address_as_after_a_free_one()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);
            var (id, session) = await ActiveOwnerAsync(host);
            await ActiveOwnerAsync(host, "bob@example.com");
            var free = await host.RequestEmailChangeAsync(id, "new@example.com");
            var start = host.Limiter.HitsOfFlow(CodeLimits.GuessPerPrincipal.Name);
            await host.ConfirmEmailChangeAsync(id, session.Id, free.Value.Id.ToString(), "000000");
            var afterFree = host.Limiter.HitsOfFlow(CodeLimits.GuessPerPrincipal.Name) - start;
            var takenRequest = await host.RequestEmailChangeAsync(id, "bob@example.com");

            // Act
            await host.ConfirmEmailChangeAsync(id, session.Id, takenRequest.Value.Id.ToString(), "000000");

            // Assert
            await Assert.That(afterFree).IsGreaterThan(0);
            await Assert.That(host.Limiter.HitsOfFlow(CodeLimits.GuessPerPrincipal.Name)).IsEqualTo(start + (afterFree * 2));
        });
    }

    private static async Task<string> LastVerificationCodeAsync(OwnersHost host) =>
        (await host.OutboxAsync()).Last(e => e.Type == Issued && e.Data.GetProperty("purpose").GetString() == "email_verification")
            .Data.GetProperty("code").GetString()!;
}
