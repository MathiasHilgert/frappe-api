using Frappe.Identity.Application.Principals;
using Frappe.Identity.Application.Sessions;
using Frappe.Identity.Domain.Principals;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Wolverine;

namespace Frappe.IntegrationTests.Identity;

/// <summary>The read port behind identity queries: no-tracking projections that apply the aggregate's own windows.</summary>
public sealed class OwnerReadsTests(ApiFixture fixture)
{
    [Fact]
    public async Task GetProfile_ProjectsTheOwner_AndIsNullForAnUnknownPrincipal()
    {
        var (principal, _, _) = await SessionSeeder.SeedAsync(fixture.Host);
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        var reads = scope.ServiceProvider.GetRequiredService<IOwnerReads>();

        var profile = await reads.GetProfileAsync(principal.Id, CancellationToken.None);

        profile.ShouldBe(new OwnerProfile(principal.Id, "Ana", principal.Owner.Email.Value, "active", "en"));
        (await reads.GetProfileAsync(Guid.CreateVersion7(), CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task ListLiveSessions_DropsIdleAndExpiredSessions_AndMarksTheCurrentOne()
    {
        var (principal, session, _) = await SessionSeeder.SeedAsync(fixture.Host);
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        var reads = scope.ServiceProvider.GetRequiredService<IOwnerReads>();

        var live = await reads.ListLiveSessionsAsync(principal.Id, session.Id, session.LastSeenAt, CancellationToken.None);
        var idle = await reads.ListLiveSessionsAsync(
            principal.Id, session.Id, session.LastSeenAt + Session.IdleTimeout, CancellationToken.None);
        var foreign = await reads.ListLiveSessionsAsync(Guid.CreateVersion7(), session.Id, session.LastSeenAt, CancellationToken.None);

        // Postgres keeps microseconds, so the stored instants are compared at that precision.
        var only = live.ShouldHaveSingleItem();
        only.Id.ShouldBe(session.Id);
        only.Current.ShouldBeTrue();
        only.CreatedAt.ShouldBe(session.CreatedAt, TimeSpan.FromMilliseconds(1));
        only.LastSeenAt.ShouldBe(session.LastSeenAt, TimeSpan.FromMilliseconds(1));
        idle.ShouldBeEmpty();
        foreign.ShouldBeEmpty();
    }

    [Fact]
    public async Task HasFreshStepUp_IsTrueOnlyInsideTheStepUpWindow()
    {
        var (principal, session, _) = await SessionSeeder.SeedAsync(fixture.Host);
        DateTimeOffset expiresAt;
        await using (var write = fixture.Host.Services.CreateAsyncScope())
        {
            var repository = write.ServiceProvider.GetRequiredService<IPrincipalRepository>();
            var loaded = (await repository.GetByIdAsync(principal.Id, CancellationToken.None)).ShouldNotBeNull();
            expiresAt = loaded.CompleteStepUp(session.Id, write.ServiceProvider.GetRequiredService<TimeProvider>()).Value;
            (await repository.SaveChangesAsync(CancellationToken.None)).IsSuccess.ShouldBeTrue();
        }

        await using var scope = fixture.Host.Services.CreateAsyncScope();
        var reads = scope.ServiceProvider.GetRequiredService<IOwnerReads>();

        (await reads.HasFreshStepUpAsync(principal.Id, session.Id, expiresAt.AddSeconds(-1), CancellationToken.None)).ShouldBeTrue();
        (await reads.HasFreshStepUpAsync(principal.Id, session.Id, expiresAt, CancellationToken.None)).ShouldBeFalse();
        (await reads.HasFreshStepUpAsync(Guid.CreateVersion7(), session.Id, expiresAt.AddSeconds(-1), CancellationToken.None)).ShouldBeFalse();
    }

    [Fact]
    public async Task GetMe_ThroughTheBus_AnswersNullForAGonePrincipal()
    {
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        var profile = await bus.InvokeAsync<OwnerProfile?>(new GetMe(Guid.CreateVersion7()), CancellationToken.None);

        profile.ShouldBeNull();
    }
}
