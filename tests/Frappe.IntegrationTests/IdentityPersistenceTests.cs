using Frappe.SharedKernel.Localization;
using Alba;
using Frappe.Identity.Application.Principals;
using Frappe.Identity.Domain;
using Frappe.Identity.Domain.Principals;
using Frappe.Identity.Domain.Users;
using Frappe.Identity.Infrastructure.Persistence;
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
        principal.IssueCode(CodePurpose.EmailVerify, "code-hash", Clock).IsSuccess.ShouldBeTrue();
        principal.VerifyEmail("code-hash", Clock).IsSuccess.ShouldBeTrue();
        principal.IssueCode(CodePurpose.PasswordReset, "reset-hash", Clock).IsSuccess.ShouldBeTrue();
        var session = principal.OpenSession(UniqueToken(), SessionSurface.Console, Clock).Value;
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
        principal.IssueCode(CodePurpose.PasswordReset, "reset-hash", Clock);
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
            return await r.SaveChangesAsync(TestContext.Current.CancellationToken);
        });

        result.IsFailure.ShouldBeTrue();
        result.Errors.ShouldBe([IdentityErrors.EmailTaken]);
    }

    [Fact]
    public async Task GetBySessionTokenHash_FindsTheOwningPrincipal()
    {
        var principal = NewOwner(UniqueEmail());
        principal.IssueCode(CodePurpose.EmailVerify, "code-hash", Clock);
        principal.VerifyEmail("code-hash", Clock);
        var token = UniqueToken();
        principal.OpenSession(token, SessionSurface.Api, Clock);
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
        principal.IssueCode(CodePurpose.EmailVerify, "code-hash", Clock);
        await SaveAsync(principal);

        await WithRepositoryAsync(async r =>
        {
            var loaded = (await r.GetByIdAsync(principal.Id, TestContext.Current.CancellationToken)).ShouldNotBeNull();
            // A resend replaces the live code of the same purpose within one save.
            loaded.IssueCode(CodePurpose.EmailVerify, "resent-hash", Clock).IsSuccess.ShouldBeTrue();
            loaded.VerifyEmail("resent-hash", Clock).IsSuccess.ShouldBeTrue();
            loaded.OpenSession(UniqueToken(), SessionSurface.Api, Clock).IsSuccess.ShouldBeTrue();
            return await r.SaveChangesAsync(TestContext.Current.CancellationToken);
        });

        var reloaded = await WithRepositoryAsync(r => r.GetByIdAsync(principal.Id, TestContext.Current.CancellationToken));
        reloaded.ShouldNotBeNull().Owner.Status.ShouldBe(OwnerStatus.Active);
        reloaded.Sessions.ShouldHaveSingleItem();
    }

    private static Principal NewOwner(string email) =>
        Principal.RegisterOwner(PersonName.Create("Ana").Value, Email(email), "password-hash", Locale.Parse("en"), Clock);

    private static Email Email(string value) => Frappe.Identity.Domain.Principals.Email.Create(value).Value;

    private static string UniqueEmail() => $"owner-{Guid.NewGuid():N}@example.com";

    private static string UniqueToken() => Guid.NewGuid().ToString("N");

    private async Task SaveAsync(Principal principal)
    {
        var result = await WithRepositoryAsync(r =>
        {
            r.Add(principal);
            return r.SaveChangesAsync(TestContext.Current.CancellationToken);
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
