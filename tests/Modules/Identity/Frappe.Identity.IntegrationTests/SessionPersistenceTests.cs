using System.Net;
using Frappe.Access.Contracts;
using Frappe.Identity.Application.Sessions;
using Frappe.Identity.Application.Sessions.AuthenticateSession;
using Frappe.Identity.Application.Sessions.ListMySessions;
using Frappe.Identity.Application.Sessions.LogOut;
using Frappe.Identity.Application.Sessions.RevokeMyOtherSessions;
using Frappe.Identity.Application.Sessions.RevokeMySession;
using Frappe.Identity.Application.Sessions.TouchSession;
using Frappe.Identity.Domain;
using Frappe.Identity.Domain.Principals;
using Frappe.Identity.Domain.Sessions;
using Frappe.Identity.Infrastructure;
using Frappe.Identity.Infrastructure.Persistence;
using Frappe.Identity.Infrastructure.Sessions;
using Frappe.Observability;
using Frappe.Persistence;
using Frappe.Results;
using Frappe.Testing.Containers;
using Frappe.UseCases;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NodaTime;

namespace Frappe.Identity.IntegrationTests;

/// <summary>
/// Sessions on a real Postgres through the module's own pipeline (S8, S9): a session is found by the hash of its token, the rule reads the
/// principal's status and stamp from the row, logging out commits the revocation and its event together, and two requests that touch the same
/// session at once leave one winner and one conflict that writes nothing.
/// </summary>
[ClassDataSource<PostgresServer>(Shared = SharedType.PerTestSession)]
[NotInParallel]
public sealed class SessionPersistenceTests(PostgresServer server)
{
    private static readonly DateTimeOffset Start = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);

    private static ServiceProvider ProviderOver(string owner, FakeTimeProvider time)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Identity"] = owner }).Build());
        services.AddSingleton<TimeProvider>(time);
        new IdentityModule().AddServices(services);
        services.AddSessions();
        services.AddSingleton<IUseCaseGate, AllowAllUseCaseGate>();
        services.AddSingleton<IAuthorizer, AllowAllAuthorizer>();
        FakeCaller.AddTo(services);
        services.AddHandlers(typeof(ISessions).Assembly);
        return services.BuildServiceProvider();
    }

    private static IdentityDbContext ContextOf(string owner) =>
        new(ModuleDatabase.Options<IdentityDbContext>(owner, IdentityDbContext.Schema));

    private static async Task<(Principal Principal, string Token, Guid SessionId)> SeedAsync(
        string owner, PrincipalStatus status = PrincipalStatus.Active, bool staleStamp = false)
    {
        var principal = Principal.Create(PrincipalKind.Owner, status, Instant.FromDateTimeOffset(Start));
        var issued = new SessionTokens().Issue();
        var session = Session.Open(
            principal.Id, issued.Hash, SessionAudience.Console, staleStamp ? Guid.CreateVersion7() : principal.CredentialStamp,
            Instant.FromDateTimeOffset(Start), Duration.FromHours(12), Duration.FromDays(30), "agent", IPNetwork.Parse("203.0.113.0/24"));
        await using var db = ContextOf(owner);
        db.Principals.Add(principal);
        db.Sessions.Add(session);
        await db.SaveChangesAsync();
        return (principal, issued.Value, session.Id);
    }

    private static async Task<Result<SessionPrincipal>> AuthenticateAsync(ServiceProvider provider, string token)
    {
        await using var scope = provider.CreateAsyncScope();
        var hash = scope.ServiceProvider.GetRequiredService<ISessionTokens>().HashOf(token)!;
        return await scope.ServiceProvider.GetRequiredService<IQueryHandler<AuthenticateSession, SessionPrincipal>>()
            .HandleAsync(new AuthenticateSession(hash), default);
    }

    [Test]
    public async Task A_session_is_found_by_the_hash_of_its_token_with_its_principal_and_how_it_was_proved()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            var (principal, token, sessionId) = await SeedAsync(owner);
            await using var provider = ProviderOver(owner, new FakeTimeProvider(Start.AddHours(1)));

            // Act
            var result = await AuthenticateAsync(provider, token);

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(result.IsSuccess).IsTrue();
                await Assert.That(result.Value.PrincipalId).IsEqualTo(principal.Id);
                await Assert.That(result.Value.SessionId).IsEqualTo(sessionId);
                await Assert.That(result.Value.Kind).IsEqualTo("owner");
                await Assert.That(result.Value.Methods.Single()).IsEqualTo("pwd");
                await Assert.That(result.Value.LastSeenAt).IsEqualTo(Instant.FromDateTimeOffset(Start));
                await Assert.That(result.Value.StepUpUntil).IsNull();
            }
        });
    }

    [Test]
    public async Task A_token_nobody_holds_a_pending_principal_and_a_stale_stamp_are_all_the_one_invalid_session()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            var (_, pending, _) = await SeedAsync(owner, PrincipalStatus.Pending);
            var (_, stale, _) = await SeedAsync(owner, staleStamp: true);
            var unknown = new SessionTokens().Issue().Value;
            await using var provider = ProviderOver(owner, new FakeTimeProvider(Start.AddHours(1)));

            // Act
            var results = new[]
            {
                await AuthenticateAsync(provider, pending),
                await AuthenticateAsync(provider, stale),
                await AuthenticateAsync(provider, unknown),
            };

            // Assert
            using (Assert.Multiple())
            {
                foreach (var result in results)
                {
                    await Assert.That(result.Errors.Single()).IsEqualTo(new UnauthenticatedError(IdentityErrorCodes.SessionInvalid));
                }
            }
        });
    }

    [Test]
    public async Task A_session_unused_past_its_idle_timeout_is_invalid_when_read_from_the_row()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            var (_, token, _) = await SeedAsync(owner);
            await using var provider = ProviderOver(owner, new FakeTimeProvider(Start.AddHours(13)));

            // Act
            var result = await AuthenticateAsync(provider, token);

            // Assert
            await Assert.That(result.Errors.Single()).IsEqualTo(new UnauthenticatedError(IdentityErrorCodes.SessionInvalid));
        });
    }

    [Test]
    public async Task Logging_out_commits_the_revocation_and_its_event_together_and_the_token_stops_working()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            var (principal, token, sessionId) = await SeedAsync(owner);
            var time = new FakeTimeProvider(Start.AddMinutes(5));
            await using var provider = ProviderOver(owner, time);

            // Act
            Result<Unit> loggedOut;
            await using (var scope = provider.CreateAsyncScope())
            {
                FakeCaller.ActAs(scope.ServiceProvider, principal.Id, sessionId);
                loggedOut = await scope.ServiceProvider.GetRequiredService<ICommandHandler<LogOut, Unit>>()
                    .HandleAsync(new LogOut(), default);
            }

            var afterwards = await AuthenticateAsync(provider, token);

            // Assert
            await using var db = ContextOf(owner);
            var row = await db.Sessions.AsNoTracking().SingleAsync(s => s.Id == sessionId);
            var events = await db.Database.SqlQuery<string>($"SELECT type AS \"Value\" FROM identity.outbox_messages").ToListAsync();
            using (Assert.Multiple())
            {
                await Assert.That(loggedOut.IsSuccess).IsTrue();
                await Assert.That(row.RevocationReason).IsEqualTo(RevocationReason.Logout);
                await Assert.That(row.RevokedAt).IsEqualTo(Instant.FromDateTimeOffset(Start.AddMinutes(5)));
                await Assert.That(events.Single()).IsEqualTo("frappe.identity.session_closed.v1");
                await Assert.That(afterwards.IsSuccess).IsFalse();
            }
        });
    }

    [Test]
    public async Task Two_requests_touching_one_session_at_once_leave_one_winner_and_a_conflict_that_writes_nothing()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            var (_, _, sessionId) = await SeedAsync(owner);
            var winnerAt = Start.AddMinutes(2);
            await using var provider = ProviderOver(owner, new FakeTimeProvider(winnerAt));
            await using var loserScope = provider.CreateAsyncScope();
            var loserUnit = loserScope.ServiceProvider.GetRequiredKeyedService<IUnitOfWork>("identity");
            await loserUnit.BeginAsync(default);
            var loserSession = await loserScope.ServiceProvider.GetRequiredService<ISessions>().FindAsync(sessionId, default);
            loserSession!.Touch(Instant.FromDateTimeOffset(Start.AddMinutes(3)), Duration.FromHours(12));

            // Act
            Result<Unit> winner;
            await using (var scope = provider.CreateAsyncScope())
            {
                winner = await scope.ServiceProvider.GetRequiredService<ICommandHandler<TouchSession, Unit>>()
                    .HandleAsync(new TouchSession(sessionId), default);
            }

            var lost = await Assert.ThrowsAsync<CommitConflictException>(() => loserUnit.CommitAsync(default));
            await loserUnit.RollbackAsync(default);

            // Assert
            await using var db = ContextOf(owner);
            var row = await db.Sessions.AsNoTracking().SingleAsync(s => s.Id == sessionId);
            using (Assert.Multiple())
            {
                await Assert.That(winner.IsSuccess).IsTrue();
                await Assert.That(lost).IsNotNull();
                await Assert.That(lost!.Answer.Code).IsEqualTo(CommitConflict.Code);
                await Assert.That(row.LastSeenAt).IsEqualTo(Instant.FromDateTimeOffset(winnerAt));
                await Assert.That(row.IdleExpiresAt).IsEqualTo(Instant.FromDateTimeOffset(winnerAt) + Duration.FromHours(12));
            }
        });
    }

    [Test]
    public async Task A_touch_that_lost_the_race_leaves_the_scope_clean_so_the_logout_that_follows_in_it_commits()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            var (principal, _, sessionId) = await SeedAsync(owner);
            await using var provider = ProviderOver(owner, new FakeTimeProvider(Start.AddMinutes(5)));
            await using var scope = provider.CreateAsyncScope();
            var unit = scope.ServiceProvider.GetRequiredKeyedService<IUnitOfWork>("identity");
            await unit.BeginAsync(default);
            (await scope.ServiceProvider.GetRequiredService<ISessions>().FindAsync(sessionId, default))!
                .Touch(Instant.FromDateTimeOffset(Start.AddMinutes(3)), Duration.FromHours(12));
            await using (var other = provider.CreateAsyncScope())
            {
                await other.ServiceProvider.GetRequiredService<ICommandHandler<TouchSession, Unit>>()
                    .HandleAsync(new TouchSession(sessionId), default);
            }

            await Assert.ThrowsAsync<CommitConflictException>(() => unit.CommitAsync(default));
            await unit.RollbackAsync(default);

            // Act
            FakeCaller.ActAs(scope.ServiceProvider, principal.Id, sessionId);
            var loggedOut = await scope.ServiceProvider.GetRequiredService<ICommandHandler<LogOut, Unit>>()
                .HandleAsync(new LogOut(), default);

            // Assert
            await using var db = ContextOf(owner);
            var row = await db.Sessions.AsNoTracking().SingleAsync(s => s.Id == sessionId);
            using (Assert.Multiple())
            {
                await Assert.That(loggedOut.IsSuccess).IsTrue();
                await Assert.That(row.RevocationReason).IsEqualTo(RevocationReason.Logout);
            }
        });
    }

    [Test]
    public async Task A_touch_then_a_logout_in_one_scope_both_commit()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            var (principal, _, sessionId) = await SeedAsync(owner);
            await using var provider = ProviderOver(owner, new FakeTimeProvider(Start.AddMinutes(5)));
            await using var scope = provider.CreateAsyncScope();

            // Act
            var touched = await scope.ServiceProvider.GetRequiredService<ICommandHandler<TouchSession, Unit>>()
                .HandleAsync(new TouchSession(sessionId), default);
            FakeCaller.ActAs(scope.ServiceProvider, principal.Id, sessionId);
            var loggedOut = await scope.ServiceProvider.GetRequiredService<ICommandHandler<LogOut, Unit>>()
                .HandleAsync(new LogOut(), default);

            // Assert
            await using var db = ContextOf(owner);
            var row = await db.Sessions.AsNoTracking().SingleAsync(s => s.Id == sessionId);
            using (Assert.Multiple())
            {
                await Assert.That(touched.IsSuccess).IsTrue();
                await Assert.That(loggedOut.IsSuccess).IsTrue();
                await Assert.That(row.LastSeenAt).IsEqualTo(Instant.FromDateTimeOffset(Start.AddMinutes(5)));
                await Assert.That(row.RevocationReason).IsEqualTo(RevocationReason.Logout);
            }
        });
    }

    [Test]
    public async Task A_session_keeps_only_the_prefix_of_the_address_and_never_a_token()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            var principal = Principal.Create(PrincipalKind.Owner, PrincipalStatus.Active, Instant.FromDateTimeOffset(Start));
            await using (var seed = ContextOf(owner))
            {
                seed.Principals.Add(principal);
                await seed.SaveChangesAsync();
            }

            await using var provider = ProviderOver(owner, new FakeTimeProvider(Start));

            // Act
            OpenedSession opened;
            await using (var scope = provider.CreateAsyncScope())
            {
                opened = scope.ServiceProvider.GetRequiredService<SessionOpener>()
                    .Open(principal.Id, principal.CredentialStamp, "Mozilla/5.0", IPAddress.Parse("203.0.113.77"));
                await scope.ServiceProvider.GetRequiredKeyedService<IUnitOfWork>("identity").CommitAsync(default);
            }

            // Assert
            await using var db = ContextOf(owner);
            var row = await db.Sessions.AsNoTracking().SingleAsync(s => s.Id == opened.Id);
            var opens = await db.Database.SqlQuery<string>($"SELECT type AS \"Value\" FROM identity.outbox_messages").ToListAsync();
            using (Assert.Multiple())
            {
                await Assert.That(row.IpPrefix).IsEqualTo(IPNetwork.Parse("203.0.113.0/24"));
                await Assert.That(row.TokenHash.SequenceEqual(new SessionTokens().HashOf(opened.Token.Value)!)).IsTrue();
                await Assert.That(row.Methods.Single()).IsEqualTo(AuthenticationMethod.Pwd);
                await Assert.That(opens.Single()).IsEqualTo("frappe.identity.session_opened.v1");
            }
        });
    }

    [Test]
    public async Task The_list_pages_live_sessions_by_keyset_over_ties_and_revoking_the_others_commits_each_revocation_with_its_event()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            var principal = Principal.Create(PrincipalKind.Owner, PrincipalStatus.Active, Instant.FromDateTimeOffset(Start));
            Session OpenedAt(int minutes) => Session.Open(
                principal.Id, new SessionTokens().Issue().Hash, SessionAudience.Console, principal.CredentialStamp,
                Instant.FromDateTimeOffset(Start.AddMinutes(minutes)), Duration.FromHours(12), Duration.FromDays(30), "agent",
                IPNetwork.Parse("203.0.113.0/24"));
            var sessions = new[] { OpenedAt(1), OpenedAt(2), OpenedAt(2), OpenedAt(3), OpenedAt(4) };
            sessions[4].Revoke(RevocationReason.Logout, Instant.FromDateTimeOffset(Start.AddMinutes(5)));
            var stranger = Principal.Create(PrincipalKind.Owner, PrincipalStatus.Active, Instant.FromDateTimeOffset(Start));
            var foreign = Session.Open(
                stranger.Id, new SessionTokens().Issue().Hash, SessionAudience.Console, stranger.CredentialStamp, Instant.FromDateTimeOffset(Start),
                Duration.FromHours(12), Duration.FromDays(30), null, null);
            await using (var seed = ContextOf(owner))
            {
                seed.Principals.Add(principal);
                seed.Principals.Add(stranger);
                await seed.SaveChangesAsync();
                seed.Sessions.AddRange(sessions);
                seed.Sessions.Add(foreign);
                await seed.SaveChangesAsync();
            }

            await using var provider = ProviderOver(owner, new FakeTimeProvider(Start.AddHours(1)));
            var expected = sessions[..4].OrderByDescending(x => x.OpenedAt).ThenBy(x => x.Id).Select(x => x.Id).ToArray();
            var current = expected[0];

            // Act
            var pages = new List<MySessionsPage>();
            SessionPosition? after = null;
            do
            {
                await using var scope = provider.CreateAsyncScope();
                FakeCaller.ActAs(scope.ServiceProvider, principal.Id, current);
                var page = (await scope.ServiceProvider.GetRequiredService<IQueryHandler<ListMySessions, MySessionsPage>>()
                    .HandleAsync(new ListMySessions(3, after), default)).Value;
                pages.Add(page);
                after = page.Next;
            }
            while (after is not null);

            Result<Unit> others;
            await using (var scope = provider.CreateAsyncScope())
            {
                FakeCaller.ActAs(scope.ServiceProvider, principal.Id, current);
                others = await scope.ServiceProvider.GetRequiredService<ICommandHandler<RevokeMyOtherSessions, Unit>>()
                    .HandleAsync(new RevokeMyOtherSessions(), default);
            }

            Result<Unit> again;
            await using (var scope = provider.CreateAsyncScope())
            {
                again = await scope.ServiceProvider.GetRequiredService<ICommandHandler<RevokeMySession, Unit>>()
                    .HandleAsync(new RevokeMySession(expected[1]), default);
            }

            // Assert
            await using var db = ContextOf(owner);
            var rows = await db.Sessions.AsNoTracking().Where(s => s.PrincipalId == principal.Id).ToListAsync();
            var events = await db.Database.SqlQuery<string>($"SELECT type AS \"Value\" FROM identity.outbox_messages").ToListAsync();
            using (Assert.Multiple())
            {
                await Assert.That(pages.Select(p => p.Items.Count).ToArray()).IsEquivalentTo([3, 1]);
                await Assert.That(pages.SelectMany(p => p.Items).Select(i => i.Id).ToArray()).IsEquivalentTo(expected);
                await Assert.That(pages[0].Items[0].Current).IsTrue();
                await Assert.That(pages[0].Items[0].IpPrefix!.ToString()).IsEqualTo("203.0.113.0/24");
                await Assert.That(others.IsSuccess).IsTrue();
                await Assert.That(rows.Single(r => r.Id == current).RevokedAt).IsNull();
                await Assert.That(rows.Count(r => r.RevocationReason == RevocationReason.OthersRevoked)).IsEqualTo(3);
                await Assert.That(again.Errors.Single()).IsEqualTo(new NotFoundError(IdentityErrorCodes.SessionNotFound));
                await Assert.That(events.Count(e => e == "frappe.identity.session_revoked.v1")).IsEqualTo(3);
                await Assert.That((await db.Sessions.AsNoTracking().SingleAsync(s => s.Id == foreign.Id)).RevokedAt).IsNull();
            }
        });
    }
}
