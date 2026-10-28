using Frappe.Kernel.Messaging;
using Frappe.Platform.IntegrationTests.Messaging.Audit.Infrastructure;
using Frappe.Platform.IntegrationTests.Messaging.Billing.Infrastructure;
using Frappe.Platform.IntegrationTests.Messaging.Kitchen.Application;
using Frappe.Platform.IntegrationTests.Messaging.Kitchen.Infrastructure;
using Frappe.Platform.IntegrationTests.Messaging.Rogue.Infrastructure;
using Frappe.TestUtilities;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;

namespace Frappe.Platform.IntegrationTests.Messaging;

[Collection(MessagingTestGroup.Name)]
public sealed class PipelineTests(MessagingFixture fixture)
{
    // Enqueued, so the command and the event it publishes run on queue listeners, away from the caller's
    // tenant scope; separated mode gives each consumer its own chain, and every chain must open the tenant.
    [Fact]
    public async Task Every_chain_opens_the_tenant_before_its_transaction_begins()
    {
        var tenant = Guid.CreateVersion7();

        await fixture.Host.TrackAsync(
            tenant,
            static services =>
                services
                    .GetRequiredService<IDispatcher>()
                    .EnqueueAsync(
                        new QueueTicket(Guid.CreateVersion7(), "ok"),
                        TestContext.Current.CancellationToken
                    )
        );

        fixture
            .Transactions.Begun.Where(begun => begun.Tenant == tenant)
            .Select(static begun => begun.Context)
            .Distinct()
            .ShouldBe(
                [typeof(KitchenDbContext), typeof(BillingDbContext), typeof(AuditDbContext)],
                ignoreOrder: true
            );
    }

    [Fact]
    public void A_module_that_breaks_a_bus_rule_fails_the_boot_saying_how_to_fix_it()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();

        InvalidOperationException error = Should.Throw<InvalidOperationException>(() =>
            builder.AddFrappeModules(new KitchenModule(), new RogueModule())
        );

        error.Message.ShouldContain(
            "Module 'rogue' handles Frappe.Platform.IntegrationTests.Messaging.Kitchen.Contracts.TicketOpened"
        );
        error.Message.ShouldContain("add it to SubscribedEvents");
        error.Message.ShouldContain(
            "Frappe.Platform.IntegrationTests.Messaging.Rogue.Application.OrphanCommand has 0 handlers"
        );
        error.Message.ShouldContain(
            "searched the public types under Frappe.Platform.IntegrationTests.Messaging.Rogue.* in Frappe.Platform.IntegrationTests"
        );
        error.Message.ShouldContain(
            "Frappe.Platform.IntegrationTests.Messaging.Rogue.Contracts.UnnamedEvent is stored by the bus but has no [MessageIdentity]"
        );
    }

    [Fact]
    public async Task Metrics_carry_no_tenant_id()
    {
        await fixture.Host.TrackAsync(
            Guid.CreateVersion7(),
            static async services =>
            {
                IDispatcher dispatcher = services.GetRequiredService<IDispatcher>();
                await dispatcher.EnqueueAsync(
                    new QueueTicket(Guid.CreateVersion7(), "ok"),
                    TestContext.Current.CancellationToken
                );
                await dispatcher.EnqueueAsync(
                    new QueueTicket(Guid.CreateVersion7(), "reject"),
                    TestContext.Current.CancellationToken
                );
            }
        );

        fixture.Host.Services.GetRequiredService<MeterProvider>().ForceFlush().ShouldBeTrue();

        MetricSnapshot[] wolverine =
        [
            .. fixture.Metrics.Where(static metric =>
                metric.MeterName.StartsWith("Wolverine", StringComparison.Ordinal)
            ),
        ];
        wolverine.ShouldContain(static metric =>
            TagKeys(metric).Contains("message.type", StringComparer.Ordinal)
        );
        wolverine
            .SelectMany(TagKeys)
            .ShouldAllBe(static key => !string.Equals(key, "tenant.id", StringComparison.Ordinal));
        fixture
            .Metrics.Where(static metric =>
                string.Equals(metric.Name, "frappe.messaging.rejections", StringComparison.Ordinal)
            )
            .SelectMany(TagKeys)
            .ShouldBe(["messaging.message.type"]);
    }

    private static IEnumerable<string> TagKeys(MetricSnapshot metric)
    {
        List<string> keys = [];
        foreach (MetricPoint point in metric.MetricPoints)
        {
            foreach (KeyValuePair<string, object?> tag in point.Tags)
            {
                keys.Add(tag.Key);
            }
        }

        return keys.Distinct(StringComparer.Ordinal);
    }
}
