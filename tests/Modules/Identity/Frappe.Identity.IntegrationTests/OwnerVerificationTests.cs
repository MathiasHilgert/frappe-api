using Frappe.Identity.Domain;
using Frappe.Identity.Domain.Principals;
using Frappe.Results;
using Frappe.Testing.Containers;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace Frappe.Identity.IntegrationTests;

/// <summary>
/// Verifying an owner's address and asking for a new code on a real Postgres (S6, S5): the right code activates the owner, proves the address and
/// opens a session in one commit; every wrong, late, used, replaced or unknown case is the one <c>identity.code.invalid</c> and changes
/// nothing; a resend always succeeds and replaces the live code within a budget of five sends an hour.
/// </summary>
[ClassDataSource<PostgresServer>(Shared = SharedType.PerTestSession)]
[NotInParallel]
public sealed class OwnerVerificationTests(PostgresServer server)
{
    private const string CodeIssued = "frappe.identity.owner_code_issued.v1";

    private static async Task AssertInvalidAsync<T>(Result<T> result) =>
        await Assert.That(result.Errors.Single()).IsEqualTo(Error.RuleViolation(IdentityErrorCodes.CodeInvalid));

    private static async Task AssertStillPendingAsync(OwnersHost host)
    {
        await using var db = host.Context();
        using (Assert.Multiple())
        {
            await Assert.That((await db.Principals.AsNoTracking().SingleAsync()).Status).IsEqualTo(PrincipalStatus.Pending);
            await Assert.That((await db.Owners.AsNoTracking().SingleAsync()).VerifiedAt).IsNull();
            await Assert.That((await db.EmailIdentifiers.AsNoTracking().SingleAsync()).VerifiedAt).IsNull();
            await Assert.That(await db.Sessions.CountAsync()).IsEqualTo(0);
        }
    }

    [Test]
    public async Task The_right_code_activates_the_owner_proves_the_address_and_opens_a_session_in_one_commit()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);
            var registered = await host.RegisterAsync();
            var code = await host.LastCodeAsync();
            host.Time.Advance(TimeSpan.FromMinutes(5));

            // Act
            var result = await host.VerifyAsync(registered.Value.Id.ToString(), code);

            // Assert
            await using var db = host.Context();
            var principal = await db.Principals.AsNoTracking().SingleAsync();
            var session = await db.Sessions.AsNoTracking().SingleAsync();
            var now = Instant.FromDateTimeOffset(OwnersHost.Start.AddMinutes(5));
            var events = (await host.OutboxAsync()).Select(e => e.Type).ToList();
            using (Assert.Multiple())
            {
                await Assert.That(result.IsSuccess).IsTrue();
                await Assert.That(result.Value.Id).IsEqualTo(session.Id);
                await Assert.That(result.Value.Token.Value).StartsWith("fs_");
                await Assert.That(principal.Status).IsEqualTo(PrincipalStatus.Active);
                await Assert.That((await db.Owners.AsNoTracking().SingleAsync()).VerifiedAt).IsEqualTo(now);
                await Assert.That((await db.EmailIdentifiers.AsNoTracking().SingleAsync()).VerifiedAt).IsEqualTo(now);
                await Assert.That((await db.OneTimeCodes.AsNoTracking().SingleAsync()).RedeemedAt).IsEqualTo(now);
                await Assert.That(session.PrincipalId).IsEqualTo(principal.Id);
                await Assert.That(session.CredentialStamp).IsEqualTo(principal.CredentialStamp);
                await Assert.That(session.IpPrefix!.ToString()).IsEqualTo("203.0.113.0/24");
                await Assert.That(events).Contains("frappe.identity.owner_verified.v1").And.Contains("frappe.identity.session_opened.v1");
            }
        });
    }

    [Test]
    public async Task A_code_that_was_used_cannot_verify_again()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);
            var registered = await host.RegisterAsync();
            var code = await host.LastCodeAsync();
            await host.VerifyAsync(registered.Value.Id.ToString(), code);

            // Act
            var again = await host.VerifyAsync(registered.Value.Id.ToString(), code);

            // Assert
            await using var db = host.Context();
            using (Assert.Multiple())
            {
                await AssertInvalidAsync(again);
                await Assert.That(await db.Sessions.CountAsync()).IsEqualTo(1);
            }
        });
    }

    [Test]
    [Arguments("000000")]
    [Arguments("12345")]
    [Arguments("abcdef")]
    [Arguments("")]
    [Arguments(null)]
    public async Task A_wrong_code_is_the_one_invalid_answer_and_changes_nothing(string? wrong)
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);
            var registered = await host.RegisterAsync();
            var code = await host.LastCodeAsync();

            // Act
            var result = await host.VerifyAsync(registered.Value.Id.ToString(), wrong == code ? "999999" : wrong);

            // Assert
            await AssertInvalidAsync(result);
            await AssertStillPendingAsync(host);
        });
    }

    [Test]
    public async Task A_code_is_invalid_after_fifteen_minutes()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);
            var registered = await host.RegisterAsync();
            var code = await host.LastCodeAsync();
            host.Time.Advance(TimeSpan.FromMinutes(15));

            // Act
            var result = await host.VerifyAsync(registered.Value.Id.ToString(), code);

            // Assert
            await AssertInvalidAsync(result);
            await AssertStillPendingAsync(host);
        });
    }

    [Test]
    public async Task A_code_replaced_by_a_resend_is_invalid_and_the_new_one_verifies()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);
            var registered = await host.RegisterAsync();
            var old = await host.LastCodeAsync();
            await host.ResendAsync(registered.Value.Id.ToString());
            var fresh = await host.LastCodeAsync();

            // Act
            var withOld = await host.VerifyAsync(registered.Value.Id.ToString(), old == fresh ? "000000" : old);
            var withNew = await host.VerifyAsync(registered.Value.Id.ToString(), fresh);

            // Assert
            using (Assert.Multiple())
            {
                await AssertInvalidAsync(withOld);
                await Assert.That(withNew.IsSuccess).IsTrue();
            }
        });
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("not-a-guid")]
    [Arguments("0198f2a0-0000-7000-8000-000000000001")]
    public async Task A_malformed_or_unknown_id_is_the_same_invalid_answer(string? id)
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);
            await host.RegisterAsync();

            // Act
            var result = await host.VerifyAsync(id, await host.LastCodeAsync());

            // Assert
            await AssertInvalidAsync(result);
            await AssertStillPendingAsync(host);
        });
    }

    [Test]
    public async Task Guesses_that_cannot_be_counted_judge_nothing_and_say_so()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);
            var registered = await host.RegisterAsync();
            var code = await host.LastCodeAsync();
            host.Limiter.Down = true;

            // Act
            var result = await host.VerifyAsync(registered.Value.Id.ToString(), code);

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(result.Errors.Single().Code).IsEqualTo(IdentityErrorCodes.AttemptsUnavailable);
                await AssertStillPendingAsync(host);
            }
        });
    }

    [Test]
    public async Task A_resend_to_a_pending_owner_mails_a_new_code_that_replaces_the_live_one()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);
            var registered = await host.RegisterAsync();

            // Act
            var result = await host.ResendAsync(registered.Value.Id.ToString());

            // Assert
            await using var db = host.Context();
            var codes = await db.OneTimeCodes.AsNoTracking().ToListAsync();
            var events = await host.OutboxAsync();
            using (Assert.Multiple())
            {
                await Assert.That(result.IsSuccess).IsTrue();
                await Assert.That(codes.Count).IsEqualTo(2);
                await Assert.That(codes.Count(c => c.HoldsLiveSlot)).IsEqualTo(1);
                await Assert.That(events.Count(e => e.Type == CodeIssued)).IsEqualTo(2);
                await Assert.That(events[^1].Data.GetProperty("email").GetString()).IsEqualTo("ana@example.com");
            }
        });
    }

    [Test]
    public async Task A_resend_beyond_five_sends_an_hour_still_succeeds_and_sends_nothing_more()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);
            var registered = await host.RegisterAsync();
            for (var sends = 1; sends < 5; sends++)
            {
                await host.ResendAsync(registered.Value.Id.ToString());
            }

            var live = await host.LastCodeAsync();

            // Act
            var sixth = await host.ResendAsync(registered.Value.Id.ToString());

            // Assert
            var events = await host.OutboxAsync();
            using (Assert.Multiple())
            {
                await Assert.That(sixth.IsSuccess).IsTrue();
                await Assert.That(events.Count(e => e.Type == CodeIssued)).IsEqualTo(5);
                await Assert.That((await host.VerifyAsync(registered.Value.Id.ToString(), live)).IsSuccess).IsTrue();
            }
        });
    }

    [Test]
    public async Task A_resend_for_an_unknown_malformed_or_verified_owner_succeeds_and_sends_nothing()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);
            var registered = await host.RegisterAsync();
            await host.VerifyAsync(registered.Value.Id.ToString(), await host.LastCodeAsync());
            var before = (await host.OutboxAsync()).Count;

            // Act
            var results = new[]
            {
                await host.ResendAsync(registered.Value.Id.ToString()),
                await host.ResendAsync(Guid.CreateVersion7().ToString()),
                await host.ResendAsync("not-a-guid"),
                await host.ResendAsync(null),
            };

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(results.All(r => r.IsSuccess)).IsTrue();
                await Assert.That((await host.OutboxAsync()).Count).IsEqualTo(before);
            }
        });
    }
}
