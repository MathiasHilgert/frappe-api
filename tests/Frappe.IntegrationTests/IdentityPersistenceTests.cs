using Frappe.Identity.Application.Principals;
using Frappe.Identity.Domain;
using Frappe.Identity.Domain.Principals;
using Frappe.Identity.Domain.Users;
using Frappe.Identity.Infrastructure.Persistence;
using Frappe.SharedKernel.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Frappe.IntegrationTests;

/// <summary>The Principal aggregate round-trips through the identity schema created by its migrations.</summary>
public sealed class IdentityPersistenceTests(ApiFixture fixture)
{
    private static readonly TimeProvider Clock = TimeProvider.System;

    [Fact]
    public async Task Principal_RoundTrips_WithItsOwnerSessionsAndCodes()
    {
        var principal = NewOwner(UniqueEmail());
        principal.IssueCode(CodePurpose.EmailVerify, "code-hash", Clock.GetUtcNow()).IsSuccess.ShouldBeTrue();
        principal.VerifyEmail("code-hash", Clock.GetUtcNow()).IsSuccess.ShouldBeTrue();
        principal.IssueCode(CodePurpose.PasswordReset, "reset-hash", Clock.GetUtcNow()).IsSuccess.ShouldBeTrue();
        var session = principal.OpenSession(UniqueToken(), SessionSurface.Console, Clock.GetUtcNow()).Value;
        await SaveAsync(principal);

        var loaded = await WithRepositoryAsync(r => r.GetByIdAsync(principal.Id, TestContext.Current.CancellationToken));

        loaded.ShouldNotBeNull();
        loaded.Type.ShouldBe(PrincipalType.Owner);
        loaded.RegisteredAt.ShouldBe(principal.RegisteredAt, TimeSpan.FromMilliseconds(1));
        loaded.Owner.Name.ShouldBe(principal.Owner.Name);
        loaded.Owner.Email.ShouldBe(principal.Owner.Email);
        loaded.Owner.PasswordHash.ShouldBe("password-hash");
        loaded.Owner.Status.ShouldBe(OwnerStatus.Active);
        loaded.Sessions.ShouldHaveSingleItem().Id.ShouldBe(session.Id);
        loaded.Sessions[0].TokenHash.ShouldBe(session.TokenHash);
        loaded.Sessions[0].Surface.ShouldBe(SessionSurface.Console);
        loaded.Codes.Select(c => c.Purpose).ShouldBe([CodePurpose.EmailVerify, CodePurpose.PasswordReset], ignoreOrder: true);
        loaded.Codes.Single(c => c.Purpose == CodePurpose.EmailVerify).UsedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Principal_StoresEnumsAsSnakeCaseText()
    {
        var principal = NewOwner(UniqueEmail());
        principal.IssueCode(CodePurpose.PasswordReset, "reset-hash", Clock.GetUtcNow());
        await SaveAsync(principal);

        var status = await ScalarAsync($"SELECT status FROM identity.owner_accounts WHERE principal_id = '{principal.Id}'");
        var purpose = await ScalarAsync($"SELECT purpose FROM identity.verification_codes WHERE principal_id = '{principal.Id}'");

        status.ShouldBe("pending");
        purpose.ShouldBe("password_reset");
    }

    [Fact]
    public async Task GetByEmail_IgnoresCase()
    {
        var email = UniqueEmail();
        var principal = NewOwner(email);
        await SaveAsync(principal);

        var loaded = await WithRepositoryAsync(r => r.GetByEmailAsync(Email(email.ToUpperInvariant()), TestContext.Current.CancellationToken));

        loaded.ShouldNotBeNull().Id.ShouldBe(principal.Id);
    }

    [Fact]
    public async Task Save_StoresTheNormalizedAddress_SoTheUniqueIndexAndLookupsAgree()
    {
        var email = UniqueEmail();
        var principal = NewOwner(email.ToUpperInvariant());
        await SaveAsync(principal);

        var stored = await ScalarAsync($"SELECT email FROM identity.owner_accounts WHERE principal_id = '{principal.Id}'");

        stored.ShouldBe(email.ToLowerInvariant());
    }

    [Fact]
    public async Task GetByEmail_ReturnsNullForAnUnknownAddress()
    {
        var loaded = await WithRepositoryAsync(r => r.GetByEmailAsync(Email(UniqueEmail()), TestContext.Current.CancellationToken));

        loaded.ShouldBeNull();
    }

    [Fact]
    public async Task Save_WithAnEmailTakenInAnotherCase_FailsWithEmailTaken()
    {
        var email = UniqueEmail();
        await SaveAsync(NewOwner(email));

        var result = await WithRepositoryAsync(async r =>
        {
            r.Add(NewOwner(email.ToUpperInvariant()));
            return await r.SaveAsync(TestContext.Current.CancellationToken);
        });

        result.IsFailure.ShouldBeTrue();
        result.Errors.ShouldBe([IdentityErrors.EmailTaken]);
    }

    [Fact]
    public async Task GetBySessionTokenHash_FindsTheOwningPrincipal()
    {
        var principal = NewOwner(UniqueEmail());
        principal.IssueCode(CodePurpose.EmailVerify, "code-hash", Clock.GetUtcNow());
        principal.VerifyEmail("code-hash", Clock.GetUtcNow());
        var token = UniqueToken();
        principal.OpenSession(token, SessionSurface.Api, Clock.GetUtcNow());
        await SaveAsync(principal);

        var loaded = await WithRepositoryAsync(r => r.GetBySessionTokenHashAsync(token, TestContext.Current.CancellationToken));
        var missing = await WithRepositoryAsync(r => r.GetBySessionTokenHashAsync(UniqueToken(), TestContext.Current.CancellationToken));

        loaded.ShouldNotBeNull().Id.ShouldBe(principal.Id);
        missing.ShouldBeNull();
    }

    [Fact]
    public async Task Save_PersistsChangesToALoadedPrincipal()
    {
        var principal = NewOwner(UniqueEmail());
        principal.IssueCode(CodePurpose.EmailVerify, "code-hash", Clock.GetUtcNow());
        await SaveAsync(principal);

        await WithRepositoryAsync(async r =>
        {
            var loaded = (await r.GetByIdAsync(principal.Id, TestContext.Current.CancellationToken)).ShouldNotBeNull();
            // A resend replaces the live code of the same purpose within one save.
            loaded.IssueCode(CodePurpose.EmailVerify, "resent-hash", Clock.GetUtcNow()).IsSuccess.ShouldBeTrue();
            loaded.VerifyEmail("resent-hash", Clock.GetUtcNow()).IsSuccess.ShouldBeTrue();
            loaded.OpenSession(UniqueToken(), SessionSurface.Api, Clock.GetUtcNow()).IsSuccess.ShouldBeTrue();
            return await r.SaveAsync(TestContext.Current.CancellationToken);
        });

        var reloaded = await WithRepositoryAsync(r => r.GetByIdAsync(principal.Id, TestContext.Current.CancellationToken));
        reloaded.ShouldNotBeNull().Owner.Status.ShouldBe(OwnerStatus.Active);
        reloaded.Sessions.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Save_OfAPrincipalChangedMeanwhile_FailsWithConcurrentChange_AndKeepsTheFirstWrite()
    {
        var (principal, sessionId) = await SeedActiveWithSessionAsync();
        await using var first = fixture.Host.Services.CreateAsyncScope();
        await using var second = fixture.Host.Services.CreateAsyncScope();
        var a = Repository(first);
        var b = Repository(second);
        var loadedA = (await a.GetByIdAsync(principal.Id, CancellationToken.None)).ShouldNotBeNull();
        var loadedB = (await b.GetByIdAsync(principal.Id, CancellationToken.None)).ShouldNotBeNull();

        loadedA.RevokeSession(sessionId).IsSuccess.ShouldBeTrue();
        (await a.SaveAsync(CancellationToken.None)).IsSuccess.ShouldBeTrue();
        // Only a child row changes here, yet the aggregate's version still guards it.
        loadedB.CompleteStepUp(sessionId, Clock.GetUtcNow()).IsSuccess.ShouldBeTrue();

        (await b.SaveAsync(CancellationToken.None)).Errors.ShouldBe([IdentityErrors.ConcurrentChange]);
        (await WithRepositoryAsync(r => r.GetByIdAsync(principal.Id, CancellationToken.None)))
            .ShouldNotBeNull().Sessions.ShouldBeEmpty();
    }

    [Fact]
    public async Task Save_ATouchRacingARevocation_FailsWithConcurrentChangeAndWritesNothing()
    {
        var (principal, sessionId) = await SeedActiveWithSessionAsync();
        await using var toucher = fixture.Host.Services.CreateAsyncScope();
        var touching = Repository(toucher);
        var loaded = (await touching.GetByIdAsync(principal.Id, CancellationToken.None)).ShouldNotBeNull();
        await WithRepositoryAsync(async r =>
        {
            var revoking = (await r.GetByIdAsync(principal.Id, CancellationToken.None)).ShouldNotBeNull();
            revoking.ResetPassword("reset-hash", "new-password-hash", Clock.GetUtcNow()).IsSuccess.ShouldBeTrue();
            (await r.SaveAsync(CancellationToken.None)).IsSuccess.ShouldBeTrue();
            return true;
        });

        loaded.TouchSession(sessionId, Clock.GetUtcNow()).IsSuccess.ShouldBeTrue();
        var saved = await touching.SaveAsync(CancellationToken.None);

        saved.Errors.ShouldBe([IdentityErrors.ConcurrentChange]);
        var reloaded = (await WithRepositoryAsync(r => r.GetByIdAsync(principal.Id, CancellationToken.None))).ShouldNotBeNull();
        reloaded.Sessions.ShouldBeEmpty();
        reloaded.Owner.PasswordHash.ShouldBe("new-password-hash");
    }

    [Fact]
    public async Task FindSessionCredential_ReadsOnlyTheSessionView_WithoutTrackingTheAggregate()
    {
        var (principal, sessionId) = await SeedActiveWithSessionAsync();
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        var tokenHash = (await Repository(scope).GetByIdAsync(principal.Id, CancellationToken.None))!.Sessions.Single().TokenHash;
        scope.ServiceProvider.GetRequiredService<IdentityDbContext>().ChangeTracker.Clear();

        var credential = await Repository(scope).FindSessionCredentialAsync(tokenHash, CancellationToken.None);
        var missing = await Repository(scope).FindSessionCredentialAsync(UniqueToken(), CancellationToken.None);

        credential.ShouldNotBeNull();
        credential.PrincipalId.ShouldBe(principal.Id);
        credential.SessionId.ShouldBe(sessionId);
        credential.PrincipalType.ShouldBe(PrincipalType.Owner);
        credential.OwnerStatus.ShouldBe(OwnerStatus.Active);
        credential.Admits(Clock).ShouldBeTrue();
        missing.ShouldBeNull();
        scope.ServiceProvider.GetRequiredService<IdentityDbContext>().ChangeTracker.Entries().ShouldBeEmpty();
    }

    private async Task<(Principal Principal, Guid SessionId)> SeedActiveWithSessionAsync()
    {
        var principal = NewOwner(UniqueEmail());
        principal.IssueCode(CodePurpose.EmailVerify, "code-hash", Clock.GetUtcNow());
        principal.VerifyEmail("code-hash", Clock.GetUtcNow());
        principal.IssueCode(CodePurpose.PasswordReset, "reset-hash", Clock.GetUtcNow());
        var session = principal.OpenSession(UniqueToken(), SessionSurface.Api, Clock.GetUtcNow()).Value;
        await SaveAsync(principal);
        return (principal, session.Id);
    }

    private static IPrincipalRepository Repository(AsyncServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IPrincipalRepository>();

    private static Principal NewOwner(string email) =>
        Principal.RegisterOwner(PersonName.Create("Ana").Value, Email(email), "password-hash", Locale.Parse("en"), Clock.GetUtcNow());

    private static Email Email(string value) => Frappe.Identity.Domain.Principals.Email.Create(value).Value;

    private static string UniqueEmail() => $"owner-{Guid.NewGuid():N}@example.com";

    private static string UniqueToken() => Guid.NewGuid().ToString("N");

    private async Task SaveAsync(Principal principal)
    {
        var result = await WithRepositoryAsync(r =>
        {
            r.Add(principal);
            return r.SaveAsync(TestContext.Current.CancellationToken);
        });
        result.IsSuccess.ShouldBeTrue();
    }

    private async Task<T> WithRepositoryAsync<T>(Func<IPrincipalRepository, Task<T>> action)
    {
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
        return await action(scope.ServiceProvider.GetRequiredService<IPrincipalRepository>());
    }

    private async Task<string?> ScalarAsync(string sql)
    {
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        var connection = scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (string?)await command.ExecuteScalarAsync();
    }
}
