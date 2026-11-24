using Frappe.Identity.Application.Sessions;
using Frappe.Identity.Contracts.Owners;
using Frappe.Identity.Domain;
using Frappe.Identity.Domain.Sessions;
using Frappe.Testing.Containers;
using Microsoft.EntityFrameworkCore;

namespace Frappe.Identity.IntegrationTests;

/// <summary>
/// Undoing an email change from the notice on a real Postgres (S15): the token of the confirmation restores the old address, proven, revokes
/// every session and kills every live code; it works once, for 7 days, never against an address another verified owner holds, and a later change
/// never takes the way back from an earlier one.
/// </summary>
[ClassDataSource<PostgresServer>(Shared = SharedType.PerTestSession)]
[NotInParallel]
public sealed class EmailChangeUndoTests(PostgresServer server)
{
    private const string Changed = "frappe.identity.owner_email_changed.v1";
    private const string Undone = "frappe.identity.owner_email_change_undone.v1";

    private static async Task<(Guid Id, OpenedSession Session)> ActiveOwnerAsync(OwnersHost host, string email = "ana@example.com")
    {
        var registered = await host.RegisterAsync(email);
        var verified = await host.VerifyAsync(registered.Value.Id.ToString(), await host.LastCodeAsync());
        await Assert.That(verified.IsSuccess).IsTrue();
        return (registered.Value.Id, verified.Value);
    }

    /// <summary>Changes the address of <paramref name="id"/> to <paramref name="newEmail"/> and returns the undo token of the notice.</summary>
    private static async Task<string> ChangeAsync(OwnersHost host, Guid id, Guid sessionId, string newEmail)
    {
        host.Time.Advance(TimeSpan.FromMinutes(1));
        var requested = await host.RequestEmailChangeAsync(id, newEmail);
        var confirmed = await host.ConfirmEmailChangeAsync(id, sessionId, requested.Value.Id.ToString(), await host.LastCodeAsync());
        await Assert.That(confirmed.IsSuccess).IsTrue();
        return (await host.OutboxAsync()).Last(e => e.Type == Changed).Data.GetProperty("undoToken").GetString()!;
    }

    [Test]
    public async Task The_notice_carries_the_token_and_the_old_address_and_the_event_prints_none_of_them()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);
            var (id, session) = await ActiveOwnerAsync(host);

            // Act
            var token = await ChangeAsync(host, id, session.Id, "new@example.com");

            // Assert
            var data = (await host.OutboxAsync()).Single(e => e.Type == Changed).Data;
            var printed = new OwnerEmailChanged(id, DateTimeOffset.UnixEpoch)
            {
                OldEmail = data.GetProperty("oldEmail").GetString(),
                UndoToken = token,
                Name = data.GetProperty("name").GetString(),
            }.ToString();
            await using var db = host.Context();
            var stored = await db.EmailChangeUndos.AsNoTracking().SingleAsync();
            using (Assert.Multiple())
            {
                await Assert.That(data.GetProperty("oldEmail").GetString()).IsEqualTo("ana@example.com");
                await Assert.That(data.GetProperty("language").GetString()).IsNotEmpty();
                await Assert.That(token.Split('.')[0]).IsEqualTo(stored.Id.ToString("N"));
                await Assert.That(stored.OldAddress).IsEqualTo("ana@example.com");
                await Assert.That(stored.NewAddress).IsEqualTo("new@example.com");
                await Assert.That(stored.ExpiresAt - stored.IssuedAt).IsEqualTo(NodaTime.Duration.FromDays(7));
                await Assert.That(printed).DoesNotContain(token).And.DoesNotContain("ana@example.com").And.Contains("UndoToken = ***");
            }
        });
    }

    [Test]
    public async Task The_token_restores_the_old_address_revokes_every_session_and_invalidates_every_live_code()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);
            var (id, session) = await ActiveOwnerAsync(host);
            var token = await ChangeAsync(host, id, session.Id, "new@example.com");
            var newSession = (await host.LogInAsync("new@example.com", OwnersHost.Password)).Value.Session!;
            await host.RequestResetAsync("new@example.com");

            // Act
            var undone = await host.UndoEmailChangeAsync(token);

            // Assert
            await using var db = host.Context();
            var identifier = await db.EmailIdentifiers.AsNoTracking().SingleAsync(e => e.PrincipalId == id);
            var live = await db.OneTimeCodes.AsNoTracking().Where(c => c.PrincipalId == id && c.RedeemedAt == null && c.InvalidatedAt == null)
                .ToListAsync();
            var revoked = await db.Sessions.AsNoTracking().Where(s => s.RevocationReason == RevocationReason.EmailChangeUndone).ToListAsync();
            using (Assert.Multiple())
            {
                await Assert.That(undone.IsSuccess).IsTrue();
                await Assert.That(identifier.NormalizedAddress).IsEqualTo("ana@example.com");
                await Assert.That(identifier.VerifiedAt).IsNotNull();
                await Assert.That(live).IsEmpty();
                await Assert.That(revoked.Select(s => s.Id)).IsEquivalentTo([session.Id, newSession.Id]);
                await Assert.That(await host.LetsInAsync(session)).IsFalse();
                await Assert.That(await host.LetsInAsync(newSession)).IsFalse();
                await Assert.That((await host.OutboxAsync()).Count(e => e.Type == Undone)).IsEqualTo(1);
                await Assert.That((await host.LogInAsync("ana@example.com", OwnersHost.Password)).IsSuccess).IsTrue();
                await Assert.That((await host.LogInAsync("new@example.com", OwnersHost.Password)).IsSuccess).IsFalse();
            }
        });
    }

    [Test]
    public async Task The_token_works_once()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);
            var (id, session) = await ActiveOwnerAsync(host);
            var token = await ChangeAsync(host, id, session.Id, "new@example.com");
            await host.UndoEmailChangeAsync(token);

            // Act
            var again = await host.UndoEmailChangeAsync(token);

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(again.Errors.Single().Code).IsEqualTo(IdentityErrorCodes.UndoInvalid);
                await Assert.That((await host.OutboxAsync()).Count(e => e.Type == Undone)).IsEqualTo(1);
            }
        });
    }

    [Test]
    public async Task The_token_stops_working_after_seven_days_and_not_before()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);
            var (id, session) = await ActiveOwnerAsync(host);
            var late = await ChangeAsync(host, id, session.Id, "new@example.com");
            host.Time.Advance(TimeSpan.FromDays(7) + TimeSpan.FromSeconds(1));
            var (otherId, otherSession) = await ActiveOwnerAsync(host, "bob@example.com");
            var early = await ChangeAsync(host, otherId, otherSession.Id, "bob2@example.com");
            host.Time.Advance(TimeSpan.FromDays(7) - TimeSpan.FromSeconds(1));

            // Act
            var expired = await host.UndoEmailChangeAsync(late);
            var inTime = await host.UndoEmailChangeAsync(early);

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(expired.Errors.Single().Code).IsEqualTo(IdentityErrorCodes.UndoInvalid);
                await Assert.That(inTime.IsSuccess).IsTrue();
            }
        });
    }

    [Test]
    public async Task An_old_address_that_another_verified_owner_holds_answers_undo_invalid_and_keeps_the_token()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);
            var (id, session) = await ActiveOwnerAsync(host);
            var token = await ChangeAsync(host, id, session.Id, "new@example.com");
            var (otherId, _) = await ActiveOwnerAsync(host, "ana@example.com");

            // Act
            var undone = await host.UndoEmailChangeAsync(token);

            // Assert
            await using var db = host.Context();
            using (Assert.Multiple())
            {
                await Assert.That(undone.Errors.Single().Code).IsEqualTo(IdentityErrorCodes.UndoInvalid);
                await Assert.That((await db.EmailIdentifiers.SingleAsync(e => e.PrincipalId == id)).NormalizedAddress).IsEqualTo("new@example.com");
                await Assert.That((await db.EmailIdentifiers.SingleAsync(e => e.PrincipalId == otherId)).NormalizedAddress)
                    .IsEqualTo("ana@example.com");
                await Assert.That((await db.EmailChangeUndos.SingleAsync()).RedeemedAt).IsNull();
                await Assert.That(await host.LetsInAsync(session)).IsTrue();
            }
        });
    }

    [Test]
    public async Task A_malformed_unknown_or_wrong_token_answers_undo_invalid_and_changes_nothing()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);
            var (id, session) = await ActiveOwnerAsync(host);
            var token = await ChangeAsync(host, id, session.Id, "new@example.com");
            var parts = token.Split('.');

            // Act
            var answers = new[]
            {
                await host.UndoEmailChangeAsync(null),
                await host.UndoEmailChangeAsync("junk"),
                await host.UndoEmailChangeAsync($"{Guid.CreateVersion7():N}.{parts[1]}"),
                await host.UndoEmailChangeAsync($"{parts[0]}.{parts[1][..^1]}x"),
                await host.UndoEmailChangeAsync($"{parts[0]}."),
            };

            // Assert
            using (Assert.Multiple())
            {
                foreach (var answer in answers)
                {
                    await Assert.That(answer.Errors.Single().Code).IsEqualTo(IdentityErrorCodes.UndoInvalid);
                }

                await Assert.That(await host.LetsInAsync(session)).IsTrue();
                await Assert.That((await host.UndoEmailChangeAsync(token)).IsSuccess).IsTrue();
            }
        });
    }

    [Test]
    public async Task A_second_change_never_takes_the_way_back_from_the_first_and_the_later_token_dies_with_the_earlier_undo()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);
            var (id, session) = await ActiveOwnerAsync(host);
            var first = await ChangeAsync(host, id, session.Id, "b@example.com");
            var second = await ChangeAsync(host, id, session.Id, "c@example.com");

            // Act
            var undoFirst = await host.UndoEmailChangeAsync(first);
            var undoSecond = await host.UndoEmailChangeAsync(second);

            // Assert
            await using var db = host.Context();
            using (Assert.Multiple())
            {
                await Assert.That(undoFirst.IsSuccess).IsTrue();
                await Assert.That((await db.EmailIdentifiers.SingleAsync(e => e.PrincipalId == id)).NormalizedAddress).IsEqualTo("ana@example.com");
                await Assert.That(undoSecond.Errors.Single().Code).IsEqualTo(IdentityErrorCodes.UndoInvalid);
            }
        });
    }

    [Test]
    public async Task Undoing_the_later_change_leaves_the_earlier_token_working()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);
            var (id, session) = await ActiveOwnerAsync(host);
            var first = await ChangeAsync(host, id, session.Id, "b@example.com");
            var second = await ChangeAsync(host, id, session.Id, "c@example.com");

            // Act
            var undoSecond = await host.UndoEmailChangeAsync(second);
            var undoFirst = await host.UndoEmailChangeAsync(first);

            // Assert
            await using var db = host.Context();
            using (Assert.Multiple())
            {
                await Assert.That(undoSecond.IsSuccess).IsTrue();
                await Assert.That(undoFirst.IsSuccess).IsTrue();
                await Assert.That((await db.EmailIdentifiers.SingleAsync(e => e.PrincipalId == id)).NormalizedAddress).IsEqualTo("ana@example.com");
            }
        });
    }
}
