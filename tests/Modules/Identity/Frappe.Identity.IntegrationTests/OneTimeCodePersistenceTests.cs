using Frappe.Identity.Application.Codes;
using Frappe.Identity.Domain.Codes;
using Frappe.Identity.Domain.Principals;
using Frappe.Identity.Infrastructure.Codes;
using Frappe.Identity.Infrastructure.Persistence;
using Frappe.Persistence;
using Frappe.Testing.Containers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NodaTime;
using Npgsql;

namespace Frappe.Identity.IntegrationTests;

/// <summary>
/// One-time codes on a real Postgres: replacing the live code is one save, in which EF writes the invalidation before the new row so the
/// unique index of live codes is never violated; and what is stored is only the hash (S5, DATA-07).
/// </summary>
[ClassDataSource<PostgresServer>(Shared = SharedType.PerTestSession)]
[NotInParallel]
public sealed class OneTimeCodePersistenceTests(PostgresServer server)
{
    private static readonly DateTimeOffset Start = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);

    private static IdentityDbContext ContextOf(string owner) =>
        new(ModuleDatabase.Options<IdentityDbContext>(owner, IdentityDbContext.Schema));

    private static HmacCodeHasher Hasher() =>
        new(Options.Create(new CodeHashOptions { Key = "bG9jYWwtb25seS1jb2RlLWhhc2gta2V5LW9mLTM2LWJ5dGVzIQ==" }));

    private static async Task<Guid> PrincipalAsync(string owner)
    {
        var principal = Principal.Create(PrincipalKind.Owner, PrincipalStatus.Pending, Instant.FromDateTimeOffset(Start));
        await using var db = ContextOf(owner);
        db.Principals.Add(principal);
        await db.SaveChangesAsync();
        return principal.Id;
    }

    private static CodeIssuer IssuerOver(IdentityDbContext db, FakeTimeProvider time) =>
        new(new EfOneTimeCodes(db), Hasher(), new AllowingLimiter(), time);

    private static async Task<List<(Guid Id, bool Live, bool Invalidated)>> RowsAsync(string owner, Guid principal)
    {
        await using var db = ContextOf(owner);
        return [.. (await db.OneTimeCodes.Where(c => c.PrincipalId == principal).ToListAsync())
            .Select(c => (c.Id, c.HoldsLiveSlot, c.InvalidatedAt is not null))];
    }

    [Test]
    public async Task Issuing_again_updates_the_old_code_before_inserting_the_new_one_in_a_single_save_and_one_live_row_remains()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            var principal = await PrincipalAsync(owner);
            var time = new FakeTimeProvider(Start);
            await using (var first = ContextOf(owner))
            {
                await IssuerOver(first, time).IssueAsync(principal, CodePurpose.EmailVerification, "ana@example.com", default);
                await first.SaveChangesAsync();
            }

            time.Advance(TimeSpan.FromMinutes(1));

            // Act
            await using var second = ContextOf(owner);
            var replaced = await IssuerOver(second, time).IssueAsync(principal, CodePurpose.EmailVerification, "ana@example.com", default);
            var saves = 0;
            second.SavedChanges += (_, args) => saves += args.EntitiesSavedCount > 0 ? 1 : 0;
            await second.SaveChangesAsync();

            // Assert
            var rows = await RowsAsync(owner, principal);
            using (Assert.Multiple())
            {
                await Assert.That(saves).IsEqualTo(1);
                await Assert.That(rows.Count).IsEqualTo(2);
                await Assert.That(rows.Count(r => r.Live)).IsEqualTo(1);
                await Assert.That(rows.Single(r => r.Live).Id).IsEqualTo(replaced.Value.Id);
                await Assert.That(rows.Single(r => !r.Live).Invalidated).IsTrue();
            }
        });
    }

    [Test]
    public async Task A_second_live_code_for_the_same_principal_and_purpose_is_refused_by_the_unique_index_when_nothing_invalidates_the_first()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            var principal = await PrincipalAsync(owner);
            var now = Instant.FromDateTimeOffset(Start);
            var hmac = Hasher().Hash("123456");
            await using (var first = ContextOf(owner))
            {
                first.OneTimeCodes.Add(OneTimeCode.Issue(principal, CodePurpose.PasswordReset, "ana@example.com", hmac, now, OneTimeCode.Lifetime));
                await first.SaveChangesAsync();
            }

            // Act
            await using var second = ContextOf(owner);
            second.OneTimeCodes.Add(OneTimeCode.Issue(principal, CodePurpose.PasswordReset, "ana@example.com", hmac, now, OneTimeCode.Lifetime));
            var refused = await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
            var postgres = refused!.InnerException as PostgresException;

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(postgres).IsNotNull();
                await Assert.That(postgres.SqlState).IsEqualTo(PostgresErrorCodes.UniqueViolation);
                await Assert.That(postgres.ConstraintName).IsEqualTo(OneTimeCodeConfiguration.LiveIndex);
            }
        });
    }

    [Test]
    public async Task A_code_that_expired_unredeemed_is_found_as_the_live_one_and_replaced_without_breaking_the_index()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            var principal = await PrincipalAsync(owner);
            var time = new FakeTimeProvider(Start);
            await using (var first = ContextOf(owner))
            {
                await IssuerOver(first, time).IssueAsync(principal, CodePurpose.EmailChange, "new@example.com", default);
                await first.SaveChangesAsync();
            }

            time.Advance(TimeSpan.FromDays(2));

            // Act
            await using var second = ContextOf(owner);
            var replaced = await IssuerOver(second, time).IssueAsync(principal, CodePurpose.EmailChange, "new@example.com", default);
            await second.SaveChangesAsync();

            // Assert
            var rows = await RowsAsync(owner, principal);
            using (Assert.Multiple())
            {
                await Assert.That(rows.Count).IsEqualTo(2);
                await Assert.That(rows.Single(r => r.Live).Id).IsEqualTo(replaced.Value.Id);
            }
        });
    }

    [Test]
    public async Task A_code_is_stored_as_its_hash_and_redeems_through_the_repository_once_with_the_row_marked()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            var principal = await PrincipalAsync(owner);
            var time = new FakeTimeProvider(Start);
            IssuedCode issued;
            await using (var first = ContextOf(owner))
            {
                issued = (await IssuerOver(first, time).IssueAsync(principal, CodePurpose.EmailVerification, "ana@example.com", default)).Value;
                await first.SaveChangesAsync();
            }

            time.Advance(TimeSpan.FromMinutes(3));

            // Act
            await using var redeeming = ContextOf(owner);
            var redeemer = new CodeRedeemer(new EfOneTimeCodes(redeeming), Hasher(), new AllowingLimiter(), time);
            var redeemed = await redeemer.RedeemAsync(principal, CodePurpose.EmailVerification, issued.Plaintext, default);
            await redeeming.SaveChangesAsync();
            await using var again = ContextOf(owner);
            var second = await new CodeRedeemer(new EfOneTimeCodes(again), Hasher(), new AllowingLimiter(), time)
                .RedeemAsync(principal, CodePurpose.EmailVerification, issued.Plaintext, default);

            // Assert
            var stored = await ScratchDatabase.ScalarAsync(owner, "SELECT encode(code_hmac, 'hex') FROM identity.one_time_codes");
            var redeemedAt = await ScratchDatabase.ScalarAsync(owner, "SELECT redeemed_at IS NOT NULL FROM identity.one_time_codes");
            using (Assert.Multiple())
            {
                await Assert.That(redeemed.Value.Target).IsEqualTo("ana@example.com");
                await Assert.That(stored).IsEqualTo(Convert.ToHexStringLower(Hasher().Hash(issued.Plaintext).ToArray()));
                await Assert.That((bool)redeemedAt!).IsTrue();
                await Assert.That(second.Errors.Single().Code).IsEqualTo("identity.code.invalid");
            }
        });
    }
}
