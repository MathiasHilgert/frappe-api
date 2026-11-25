using Frappe.Identity.Application.Owners.RegisterOwner;
using Frappe.Identity.Application.Telemetry;
using Frappe.Identity.Domain;
using Frappe.Identity.Domain.Principals;
using Frappe.Results;
using Frappe.Testing.Containers;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace Frappe.Identity.IntegrationTests;

/// <summary>
/// Registering an owner on a real Postgres through the module's own pipeline (S3, S17): a new address makes a pending owner and mails a code, an
/// address a verified owner holds answers the same and writes nothing, a pending owner is taken over by the newest registration, and two
/// registrations racing for one address both answer success while one owner and one code are written.
/// </summary>
[ClassDataSource<PostgresServer>(Shared = SharedType.PerTestSession)]
[NotInParallel]
public sealed class OwnerRegistrationTests(PostgresServer server)
{
    private const string Registered = "frappe.identity.owner_registered.v1";
    private const string CodeIssued = "frappe.identity.owner_code_issued.v1";
    private const string Attempted = "frappe.identity.owner_registration_attempted.v1";

    [Test]
    public async Task A_new_address_makes_a_pending_owner_with_its_password_and_one_live_code_and_two_events()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);

            // Act
            var result = await host.RegisterAsync("Ana.Perez@Example.com");

            // Assert
            await using var db = host.Context();
            var principal = await db.Principals.AsNoTracking().SingleAsync();
            var identifier = await db.EmailIdentifiers.AsNoTracking().SingleAsync();
            var row = await db.Owners.AsNoTracking().SingleAsync();
            var codes = await db.OneTimeCodes.AsNoTracking().ToListAsync();
            var credentials = await db.PasswordCredentials.AsNoTracking().ToListAsync();
            var events = await host.OutboxAsync();
            var issued = events.Single(e => e.Type == CodeIssued).Data;
            using (Assert.Multiple())
            {
                await Assert.That(result.IsSuccess).IsTrue();
                await Assert.That(result.Value.Id).IsEqualTo(principal.Id);
                await Assert.That(principal.Status).IsEqualTo(PrincipalStatus.Pending);
                await Assert.That(identifier.Address).IsEqualTo("Ana.Perez@Example.com");
                await Assert.That(identifier.NormalizedAddress).IsEqualTo("ana.perez@example.com");
                await Assert.That(identifier.VerifiedAt).IsNull();
                await Assert.That(row.PrincipalId).IsEqualTo(principal.Id);
                await Assert.That(row.GivenName).IsEqualTo("Ana");
                await Assert.That(row.FamilyName).IsEqualTo("Pérez");
                await Assert.That(row.Locale.Value).IsEqualTo("es-AR");
                await Assert.That(row.RegisteredAt).IsEqualTo(Instant.FromDateTimeOffset(OwnersHost.Start));
                await Assert.That(credentials.Single().Hash).StartsWith("$argon2id$").And.DoesNotContain(OwnersHost.Password);
                await Assert.That(codes.Single().HoldsLiveSlot).IsTrue();
                await Assert.That(events.Select(e => e.Type)).IsEquivalentTo([Registered, CodeIssued]);
                await Assert.That(issued.GetProperty("ownerId").GetGuid()).IsEqualTo(principal.Id);
                await Assert.That(issued.GetProperty("purpose").GetString()).IsEqualTo("email_verification");
                await Assert.That(issued.GetProperty("email").GetString()).IsEqualTo("Ana.Perez@Example.com");
                await Assert.That(issued.GetProperty("name").GetString()).IsEqualTo("Ana Pérez");
                await Assert.That(issued.GetProperty("language").GetString()).IsEqualTo("es-AR");
                await Assert.That(issued.GetProperty("code").GetString()).Matches("^[0-9]{6}$");
            }
        });
    }

    [Test]
    public async Task The_code_is_stored_only_as_its_hash()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);
            await host.RegisterAsync();

            // Act
            var code = await host.LastCodeAsync();

            // Assert
            await using var db = host.Context();
            var stored = Convert.ToHexString((await db.OneTimeCodes.AsNoTracking().SingleAsync()).CodeHmac.ToArray());
            await Assert.That(stored).DoesNotContain(code);
        });
    }

    [Test]
    public async Task A_refused_registration_writes_nothing_and_reports_every_failure_together()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);

            // Act
            var result = await host.RegisterAsync("nobody", given: "visit www.x.io", password: "short");

            // Assert
            await using var db = host.Context();
            using (Assert.Multiple())
            {
                await Assert.That(result.Errors.Select(e => e.Code)).IsEquivalentTo(
                    [IdentityErrorCodes.EmailInvalid, IdentityErrorCodes.NameInvalid, IdentityErrorCodes.PasswordTooShort]);
                await Assert.That(await db.Principals.CountAsync()).IsEqualTo(0);
                await Assert.That(await host.OutboxAsync()).IsEmpty();
            }
        });
    }

    [Test]
    public async Task An_address_a_verified_owner_holds_answers_the_same_writes_no_row_and_tells_the_real_owner()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);
            var first = await host.RegisterAsync("ana@example.com", given: "Ana", locale: "es-AR");
            await host.VerifyAsync(first.Value.Id.ToString(), await host.LastCodeAsync());
            var before = (await host.OutboxAsync()).Count;

            // Act
            var again = await host.RegisterAsync("ANA@example.com", given: "Mallory", password: "another long password", locale: "pt-BR");

            // Assert
            await using var db = host.Context();
            var events = (await host.OutboxAsync()).Skip(before).ToList();
            var attempt = events.Single().Data;
            using (Assert.Multiple())
            {
                await Assert.That(again.IsSuccess).IsTrue();
                await Assert.That(again.Value.Id).IsNotEqualTo(first.Value.Id);
                await Assert.That(await db.Principals.CountAsync()).IsEqualTo(1);
                await Assert.That(await db.Owners.CountAsync(o => o.GivenName == "Mallory")).IsEqualTo(0);
                await Assert.That(await db.PasswordCredentials.CountAsync()).IsEqualTo(1);
                await Assert.That(await db.OneTimeCodes.CountAsync()).IsEqualTo(1);
                await Assert.That(events.Single().Type).IsEqualTo(Attempted);
                await Assert.That(attempt.GetProperty("email").GetString()).IsEqualTo("ana@example.com");
                await Assert.That(attempt.GetProperty("language").GetString()).IsEqualTo("es-AR");
            }
        });
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Notices_to_a_verified_owner_are_bounded_per_address_and_every_registration_still_answers_201(bool limiterDown)
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);
            var first = await host.RegisterAsync("ana@example.com");
            await host.VerifyAsync(first.Value.Id.ToString(), await host.LastCodeAsync());
            host.Limiter.Down = limiterDown;
            var before = (await host.OutboxAsync()).Count;
            var tries = RegistrationLimits.Notice.Limit + 2;

            // Act
            var answers = new List<Result<RegisteredOwner>>();
            for (var i = 0; i < tries; i++)
            {
                answers.Add(await host.RegisterAsync(i % 2 == 0 ? "ana@example.com" : "ANA@Example.com"));
            }

            // Assert
            var notices = (await host.OutboxAsync()).Skip(before).Count(e => e.Type == Attempted);
            using (Assert.Multiple())
            {
                await Assert.That(answers.All(a => a.IsSuccess)).IsTrue();
                await Assert.That(notices).IsEqualTo(limiterDown ? tries : RegistrationLimits.Notice.Limit);
            }
        });
    }

    [Test]
    public async Task A_new_registration_replaces_a_pending_owner_and_the_old_code_stops_working()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);
            var first = await host.RegisterAsync("ana@example.com", given: "Ana", locale: "es-AR");
            var oldCode = await host.LastCodeAsync();
            host.Time.Advance(TimeSpan.FromDays(3));

            // Act
            using var counts = new Counts();
            var second = await host.RegisterAsync("ana@example.com", given: "Luz", family: null, password: "another long password", locale: "pt-BR");
            var newCode = await host.LastCodeAsync();
            var withOld = await host.VerifyAsync(first.Value.Id.ToString(), oldCode);
            var withOldCodeOnNewId = await host.VerifyAsync(second.Value.Id.ToString(), oldCode);

            // Assert
            await using var db = host.Context();
            var row = await db.Owners.AsNoTracking().SingleAsync();
            var codes = await db.OneTimeCodes.AsNoTracking().ToListAsync();
            var events = await host.OutboxAsync();
            using (Assert.Multiple())
            {
                await Assert.That(second.Value.Id).IsNotEqualTo(first.Value.Id);
                await Assert.That(row.PrincipalId).IsEqualTo(second.Value.Id);
                await Assert.That(row.GivenName).IsEqualTo("Luz");
                await Assert.That(row.FamilyName).IsNull();
                await Assert.That(row.Locale.Value).IsEqualTo("pt-BR");
                await Assert.That(row.RegisteredAt).IsEqualTo(Instant.FromDateTimeOffset(OwnersHost.Start.AddDays(3)));
                await Assert.That(await db.Principals.CountAsync()).IsEqualTo(1);
                await Assert.That(await db.PasswordCredentials.CountAsync()).IsEqualTo(1);
                await Assert.That(codes.Count(c => c.HoldsLiveSlot)).IsEqualTo(1);
                await Assert.That(codes.Single().PrincipalId).IsEqualTo(second.Value.Id);
                await Assert.That(newCode).IsNotEqualTo(oldCode);
                await Assert.That(withOld.Errors.Single().Code).IsEqualTo(IdentityErrorCodes.CodeInvalid);
                await Assert.That(withOldCodeOnNewId.Errors.Single().Code).IsEqualTo(IdentityErrorCodes.CodeInvalid);
                await Assert.That(counts.Of(OwnersMetrics.RegistrationsMetric, "replaced")).IsEqualTo(1);
                await Assert.That(counts.Of(CodesMetrics.CodesIssuedMetric, "email_verification")).IsEqualTo(1);
                await Assert.That(counts.Of(OwnersMetrics.VerificationsMetric, "invalid")).IsEqualTo(2);
                await Assert.That(events.Count(e => e.Type == Registered)).IsEqualTo(2);
                await Assert.That(events.Last(e => e.Type == CodeIssued).Data.GetProperty("name").GetString()).IsEqualTo("Luz");
            }
        });
    }

    [Test]
    public async Task The_password_of_the_newest_registration_is_the_one_that_stands()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);
            await host.RegisterAsync("ana@example.com");
            await using var before = host.Context();
            var oldHash = (await before.PasswordCredentials.AsNoTracking().SingleAsync()).Hash;

            // Act
            var second = await host.RegisterAsync("ana@example.com", password: "another long password");

            // Assert
            await using var db = host.Context();
            var credential = await db.PasswordCredentials.AsNoTracking().SingleAsync();
            using (Assert.Multiple())
            {
                await Assert.That(credential.PrincipalId).IsEqualTo(second.Value.Id);
                await Assert.That(credential.Hash).IsNotEqualTo(oldHash);
                await Assert.That(credential.ChangedAt).IsEqualTo(Instant.FromDateTimeOffset(OwnersHost.Start));
            }
        });
    }

    [Test]
    public async Task A_pending_owner_over_its_send_budget_is_replaced_all_the_same_by_a_new_principal_with_a_fresh_budget()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);
            var first = await host.RegisterAsync("ana@example.com", given: "Mallory", password: "mallory long password");
            for (var sends = 1; sends < 5; sends++)
            {
                await host.ResendAsync(first.Value.Id.ToString());
            }

            var mallorysCode = await host.LastCodeAsync();

            // Act
            var again = await host.RegisterAsync("ana@example.com", given: "Ana");
            var anasCode = await host.LastCodeAsync();
            var withMallorysCode = await host.VerifyAsync(first.Value.Id.ToString(), mallorysCode);
            var withMallorysCodeOnAnasId = await host.VerifyAsync(again.Value.Id.ToString(), mallorysCode);
            var verified = await host.VerifyAsync(again.Value.Id.ToString(), anasCode);

            // Assert
            await using var db = host.Context();
            using (Assert.Multiple())
            {
                await Assert.That(again.IsSuccess).IsTrue();
                await Assert.That(again.Value.Id).IsNotEqualTo(first.Value.Id);
                await Assert.That(withMallorysCode.IsSuccess).IsFalse();
                await Assert.That(withMallorysCodeOnAnasId.IsSuccess).IsFalse();
                await Assert.That(verified.IsSuccess).IsTrue();
                await Assert.That((await db.Owners.AsNoTracking().SingleAsync()).GivenName).IsEqualTo("Ana");
                await Assert.That(await db.PasswordCredentials.CountAsync()).IsEqualTo(1);
                await Assert.That((await host.OutboxAsync()).Count(e => e.Type == CodeIssued)).IsEqualTo(6);
            }
        });
    }

    [Test]
    public async Task Every_registration_of_an_address_gets_a_new_id_whatever_state_the_address_is_in()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);
            var ids = new List<Guid>();

            // Act
            var created = await host.RegisterAsync("ana@example.com");
            var pending = await host.RegisterAsync("ana@example.com");
            await host.VerifyAsync(pending.Value.Id.ToString(), await host.LastCodeAsync());
            var takenOnce = await host.RegisterAsync("ana@example.com");
            var takenTwice = await host.RegisterAsync("ana@example.com");
            ids.AddRange([created.Value.Id, pending.Value.Id, takenOnce.Value.Id, takenTwice.Value.Id]);

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(ids.Distinct().Count()).IsEqualTo(4);
                await Assert.That(ids.All(id => id.Version == 7)).IsTrue();
            }
        });
    }

    [Test]
    public async Task Registrations_count_by_outcome_and_a_taken_address_counts_no_code()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);
            using var counts = new Counts();

            // Act
            var first = await host.RegisterAsync("ana@example.com");
            await host.VerifyAsync(first.Value.Id.ToString(), await host.LastCodeAsync());
            await host.RegisterAsync("ana@example.com");
            await host.RegisterAsync("luz@example.com");

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(counts.Of(OwnersMetrics.RegistrationsMetric, "created")).IsEqualTo(2);
                await Assert.That(counts.Of(OwnersMetrics.RegistrationsMetric, "taken")).IsEqualTo(1);
                await Assert.That(counts.Of(OwnersMetrics.RegistrationsMetric, "replaced")).IsEqualTo(0);
                await Assert.That(counts.Of(CodesMetrics.CodesIssuedMetric, "email_verification")).IsEqualTo(2);
                await Assert.That(counts.Of(OwnersMetrics.VerificationsMetric, "verified")).IsEqualTo(1);
            }
        });
    }

    [Test]
    public async Task Two_registrations_racing_for_one_new_address_both_succeed_and_one_owner_and_one_code_are_written()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            var gate = new OwnersHost.RaceGate(2);
            await using var host = OwnersHost.Over(owner, gate);

            // Act
            var results = await Task.WhenAll(
                host.RegisterAsync("ana@example.com", given: "Ana"), host.RegisterAsync("ana@example.com", given: "Mallory"));

            // Assert
            await using var db = host.Context();
            var events = await host.OutboxAsync();
            var winner = await db.Principals.AsNoTracking().SingleAsync();
            var verified = await host.VerifyAsync(winner.Id.ToString(), await host.LastCodeAsync());
            using (Assert.Multiple())
            {
                await Assert.That(results.All(r => r.IsSuccess)).IsTrue();
                await Assert.That(await db.Owners.CountAsync()).IsEqualTo(1);
                await Assert.That(await db.EmailIdentifiers.CountAsync()).IsEqualTo(1);
                await Assert.That(await db.PasswordCredentials.CountAsync()).IsEqualTo(1);
                await Assert.That(await db.OneTimeCodes.CountAsync()).IsEqualTo(1);
                await Assert.That(events.Count(e => e.Type == Registered)).IsEqualTo(1);
                await Assert.That(events.Count(e => e.Type == CodeIssued)).IsEqualTo(1);
                await Assert.That(verified.IsSuccess).IsTrue();
            }
        });
    }

    [Test]
    public async Task Two_registrations_racing_to_replace_one_pending_owner_both_succeed_and_one_replacement_stands()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var seed = OwnersHost.Over(owner);
            var first = await seed.RegisterAsync("ana@example.com", given: "Ana");
            await using var host = OwnersHost.Over(owner, new OwnersHost.RaceGate(2));

            // Act
            var results = await Task.WhenAll(
                host.RegisterAsync("ana@example.com", given: "Luz"), host.RegisterAsync("ana@example.com", given: "Mallory"));

            // Assert
            await using var db = host.Context();
            var events = await host.OutboxAsync();
            var codes = await db.OneTimeCodes.AsNoTracking().ToListAsync();
            var standing = await db.Principals.AsNoTracking().SingleAsync();
            var replaced = await host.VerifyAsync(standing.Id.ToString(), await host.LastCodeAsync());
            using (Assert.Multiple())
            {
                await Assert.That(results.All(r => r.IsSuccess)).IsTrue();
                await Assert.That(standing.Id).IsNotEqualTo(first.Value.Id);
                await Assert.That(events.Count(e => e.Type == Registered)).IsEqualTo(2);
                await Assert.That(events.Count(e => e.Type == CodeIssued)).IsEqualTo(2);
                await Assert.That(codes.Count(c => c.HoldsLiveSlot)).IsEqualTo(1);
                await Assert.That(replaced.IsSuccess).IsTrue();
            }
        });
    }
}
