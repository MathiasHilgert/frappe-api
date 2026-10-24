using Alba;
using Frappe.Identity.Application.Principals;
using Frappe.Identity.Application.Security;
using Frappe.Identity.Domain.Principals;
using Frappe.Identity.Domain.Users;
using Frappe.Identity.Infrastructure.Persistence;
using Frappe.SharedKernel.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Frappe.IntegrationTests.Identity;

/// <summary>Stores an owner with a real password hash straight through the repository, in the requested status.</summary>
internal static class OwnerSeeder
{
    public const string Password = "correct horse battery";

    public static string NewEmail() => $"owner-{Guid.NewGuid():N}@example.com";

    public static async Task<Principal> SeedAsync(IAlbaHost host, string email, OwnerStatus status = OwnerStatus.Active)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        await services.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
        var clock = services.GetRequiredService<TimeProvider>();
        var hash = await services.GetRequiredService<IPasswordHasher>().HashAsync(Password, CancellationToken.None);

        var principal = Principal.RegisterOwner(PersonName.Create("Ana").Value, Email.Create(email).Value, hash, Locale.Parse("en"), clock);
        if (status != OwnerStatus.Pending)
        {
            principal.IssueCode(CodePurpose.EmailVerify, "code-hash", clock);
            principal.VerifyEmail("code-hash", clock).IsSuccess.ShouldBeTrue();
        }
        if (status == OwnerStatus.Suspended)
            principal.Suspend();

        var repository = services.GetRequiredService<IPrincipalRepository>();
        repository.Add(principal);
        (await repository.SaveAsync(TestContext.Current.CancellationToken)).IsSuccess.ShouldBeTrue();
        return principal;
    }
}
