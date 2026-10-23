using Alba;
using Frappe.Identity.Application.Principals;
using Frappe.Identity.Application.Security;
using Frappe.Identity.Domain.Principals;
using Frappe.Identity.Domain.Users;
using Frappe.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Frappe.IntegrationTests.Identity;

/// <summary>Stores an active owner with one open session straight through the repository, as login will later.</summary>
internal static class SessionSeeder
{
    public static async Task<(Principal Principal, Session Session, string Token)> SeedAsync(IAlbaHost host, string? password = null)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        await services.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
        var clock = services.GetRequiredService<TimeProvider>();
        var token = services.GetRequiredService<ISecretGenerator>().NewSessionToken();

        var email = Email.Create($"owner-{Guid.NewGuid():N}@example.com").Value;
        var passwordHash = password is null ? "password-hash" : services.GetRequiredService<IPasswordHasher>().Hash(password);
        var principal = Principal.RegisterOwner(PersonName.Create("Ana").Value, email, passwordHash, clock);
        principal.IssueCode(CodePurpose.EmailVerify, "code-hash", clock);
        principal.VerifyEmail("code-hash", clock).IsSuccess.ShouldBeTrue();
        var session = principal.OpenSession(services.GetRequiredService<ISecretHasher>().Hash(token), SessionSurface.Console, clock).Value;

        var repository = services.GetRequiredService<IPrincipalRepository>();
        repository.Add(principal);
        (await repository.SaveChangesAsync(CancellationToken.None)).IsSuccess.ShouldBeTrue();
        return (principal, session, token);
    }

    /// <summary>Opens another session for the same owner, as a second device would.</summary>
    public static async Task<(Session Session, string Token)> OpenSessionAsync(IAlbaHost host, Guid principalId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var token = services.GetRequiredService<ISecretGenerator>().NewSessionToken();
        var repository = services.GetRequiredService<IPrincipalRepository>();
        var principal = (await repository.GetByIdAsync(principalId, CancellationToken.None)).ShouldNotBeNull();
        var session = principal.OpenSession(services.GetRequiredService<ISecretHasher>().Hash(token), SessionSurface.Api,
            services.GetRequiredService<TimeProvider>()).Value;
        (await repository.SaveChangesAsync(CancellationToken.None)).IsSuccess.ShouldBeTrue();
        return (session, token);
    }

    public static async Task<Principal> LoadAsync(IAlbaHost host, Guid principalId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IPrincipalRepository>();
        return (await repository.GetByIdAsync(principalId, CancellationToken.None)).ShouldNotBeNull();
    }

    public static async Task RevokeAsync(IAlbaHost host, Guid principalId, Guid sessionId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IPrincipalRepository>();
        var principal = (await repository.GetByIdAsync(principalId, CancellationToken.None)).ShouldNotBeNull();
        principal.RevokeSession(sessionId).IsSuccess.ShouldBeTrue();
        (await repository.SaveChangesAsync(CancellationToken.None)).IsSuccess.ShouldBeTrue();
    }

    public static async Task SuspendAsync(IAlbaHost host, Guid principalId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IPrincipalRepository>();
        var principal = (await repository.GetByIdAsync(principalId, CancellationToken.None)).ShouldNotBeNull();
        principal.Suspend();
        (await repository.SaveChangesAsync(CancellationToken.None)).IsSuccess.ShouldBeTrue();
    }
}
