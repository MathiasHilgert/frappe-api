using Alba;
using Frappe.Identity.Application.Principals;
using Frappe.Identity.Domain.Principals;
using Frappe.Identity.Domain.Users;
using Frappe.Identity.Infrastructure.Persistence;
using Frappe.SharedKernel.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using Shouldly;
using Wolverine.Tracking;

namespace Frappe.IntegrationTests.Identity;

public sealed class PurgePendingOwnersTests(ApiFixture fixture)
{
    [Fact]
    public async Task Purge_DeletesOnlyPendingOwnersPastGrace_AndPublishesPendingPurged()
    {
        var host = fixture.Host;
        var longAgo = TestTime.NewClock(); // 2026-01-01, far past the 7-day grace for the real clock.
        var oldPending = await SeedAsync(host, longAgo, activate: false);
        var freshPending = await SeedAsync(host, TimeProvider.System, activate: false);
        var oldActive = await SeedAsync(host, longAgo, activate: true);
        var (suspended, _, _) = await SessionSeeder.SeedAsync(host);
        await SessionSeeder.SuspendAsync(host, suspended.Id);

        var tracked = await host.InvokeMessageAndWaitAsync(new PurgePendingOwners());

        (await ExistsAsync(host, oldPending)).ShouldBeFalse();
        (await ExistsAsync(host, freshPending)).ShouldBeTrue();
        (await ExistsAsync(host, oldActive)).ShouldBeTrue();
        (await ExistsAsync(host, suspended.Id)).ShouldBeTrue();
        var published = Published(tracked);
        published.ShouldContain(new OwnerPendingPurged(oldPending));
        published.ShouldNotContain(new OwnerPendingPurged(freshPending));
        published.ShouldNotContain(new OwnerPendingPurged(oldActive));
    }

    [Fact]
    public async Task Purge_IsScheduledDailyAtUtc()
    {
        var scheduler = await fixture.Host.Services.GetRequiredService<ISchedulerFactory>().GetScheduler(TestContext.Current.CancellationToken);

        var trigger = (ICronTrigger)(await scheduler.GetTrigger(new TriggerKey(nameof(PurgePendingOwners)), TestContext.Current.CancellationToken)).ShouldNotBeNull();

        trigger.CronExpressionString.ShouldBe("0 0 3 * * ?");
        trigger.TimeZone.ShouldBe(TimeZoneInfo.Utc);
    }

    // No module subscribes yet (analytics will), so Wolverine records the cascade as "no routes" rather than "sent".
    private static List<OwnerPendingPurged> Published(ITrackedSession tracked) =>
    [
        .. tracked.AllRecordsInOrder()
            .Where(r => r.MessageEventType is MessageEventType.Sent or MessageEventType.NoRoutes)
            .Select(r => r.Envelope!.Message)
            .OfType<OwnerPendingPurged>(),
    ];

    private static async Task<Guid> SeedAsync(IAlbaHost host, TimeProvider clock, bool activate)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        await services.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();

        var email = Email.Create($"purge-{Guid.NewGuid():N}@example.com").Value;
        var principal = Principal.RegisterOwner(PersonName.Create("Ana").Value, email, "password-hash", Locale.Parse("en"), clock.GetUtcNow());
        principal.IssueCode(CodePurpose.EmailVerify, "code-hash", clock.GetUtcNow());
        if (activate)
            principal.VerifyEmail("code-hash", clock.GetUtcNow()).IsSuccess.ShouldBeTrue();

        var repository = services.GetRequiredService<IPrincipalRepository>();
        repository.Add(principal);
        (await repository.SaveAsync(TestContext.Current.CancellationToken)).IsSuccess.ShouldBeTrue();
        return principal.Id;
    }

    private static async Task<bool> ExistsAsync(IAlbaHost host, Guid principalId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Principals.AnyAsync(p => p.Id == principalId);
    }
}
