using Frappe.Kernel.Messaging;
using Frappe.Platform.IntegrationTests.Messaging.Kitchen.Application;
using Frappe.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using Wolverine;
using Wolverine.Runtime;

namespace Frappe.Platform.IntegrationTests.Messaging;

// Every other messaging test runs Solo; this one proves the deployed mode does the same work.
public sealed class BalancedTests(BalancedFixture fixture) : IClassFixture<BalancedFixture>
{
    [Fact]
    public async Task A_deployed_host_runs_balanced_and_handles_invoked_and_enqueued_commands()
    {
        var tenant = Guid.CreateVersion7();
        var invoked = Guid.CreateVersion7();
        var queued = Guid.CreateVersion7();

        await fixture.Host.TrackAsync(
            tenant,
            async services =>
            {
                IDispatcher dispatcher = services.GetRequiredService<IDispatcher>();
                (
                    await dispatcher.InvokeAsync(
                        new OpenTicket(invoked, "ok"),
                        TestContext.Current.CancellationToken
                    )
                ).Value.ShouldBe(invoked);
                await dispatcher.EnqueueAsync(
                    new QueueTicket(queued, "ok"),
                    TestContext.Current.CancellationToken
                );
            }
        );

        fixture
            .Host.Services.GetRequiredService<IWolverineRuntime>()
            .Options.Durability.Mode.ShouldBe(DurabilityMode.Balanced);
        (
            await fixture.CountAsync("SELECT count(*) FROM kitchen.tickets WHERE id = $1", invoked)
        ).ShouldBe(1);
        (
            await fixture.CountAsync("SELECT count(*) FROM kitchen.tickets WHERE id = $1", queued)
        ).ShouldBe(1);
        (
            await fixture.CountAsync(
                "SELECT count(*) FROM billing.invoices WHERE business_id = $1",
                tenant
            )
        ).ShouldBe(2);
    }
}
