using System.Reflection;
using Frappe.Platform.IntegrationTests.Messaging.Audit.Infrastructure;
using Frappe.Platform.IntegrationTests.Messaging.Billing.Infrastructure;
using Frappe.Platform.IntegrationTests.Messaging.Kitchen.Infrastructure;
using Frappe.Platform.Messaging;
using Frappe.Platform.Persistence;
using Frappe.TestUtilities;
using Microsoft.Extensions.Hosting;
using Wolverine.Util;

namespace Frappe.Platform.IntegrationTests.Messaging;

// Registration and boot checks only: no database is needed.
public sealed class MessagingRegistrationTests
{
    // A context registered before the bus would use a plain AddDbContext and write outside the outbox
    // transaction, so its events could be lost or sent for rolled-back work.
    [Fact]
    public void Messaging_registered_after_a_context_fails_naming_the_context()
    {
        HostApplicationBuilder builder = NewBuilder();
        builder.AddPersistence();

        InvalidOperationException error = Should.Throw<InvalidOperationException>(() =>
            builder.AddMessaging()
        );

        error.Message.ShouldContain(nameof(PlatformDbContext));
        error.Message.ShouldContain("before AddPersistence");
    }

    // Each local queue's handlers hold a pooled connection inside their transaction, so queues that can run
    // more handlers at once than the pool has connections would starve each other and every request.
    [Fact]
    public async Task A_bus_whose_queues_outnumber_the_pool_fails_the_boot_naming_both_numbers()
    {
        InvalidOperationException error = await Should.ThrowAsync<InvalidOperationException>(() =>
            FrappeTestHost.StartAsync(
                "Host=127.0.0.1;Port=1;Username=frappe;Password=x;Database=frappe;Maximum Pool Size=4",
                [new KitchenModule(), new BillingModule(), new AuditModule()]
            )
        );

        error.Message.ShouldContain("Maximum Pool Size");
        error.Message.ShouldContain("pool of 4");
    }

    // A NATS listener runs its handlers on the listener when one module subscribes, so its parallelism holds
    // connections too: Kitchen, Billing and Audit have 5 local queues (20 handlers) and one listener (4 more).
    [Fact]
    public async Task The_connection_budget_counts_the_nats_listeners()
    {
        InvalidOperationException error = await Should.ThrowAsync<InvalidOperationException>(() =>
            FrappeTestHost.StartAsync(
                "Host=127.0.0.1;Port=1;Username=frappe;Password=x;Database=frappe;Maximum Pool Size=20",
                [new KitchenModule(), new BillingModule(), new AuditModule()]
            )
        );

        error.Message.ShouldContain("1 NATS listeners");
        error.Message.ShouldContain("pool of 20");
    }

    // Wolverine keeps its naming strategies in one process-wide list, which every host built in the
    // process would otherwise grow.
    [Fact]
    public void The_kernel_identity_naming_is_installed_once_per_process()
    {
        NewBuilder().AddMessaging();
        NewBuilder().AddMessaging();

        NamingStrategies().OfType<KernelMessageIdentityNaming>().Count().ShouldBe(1);
    }

    private static HostApplicationBuilder NewBuilder() =>
        Host.CreateApplicationBuilder(
            new HostApplicationBuilderSettings { DisableDefaults = true }
        );

    // Read by reflection: Wolverine exposes no way to list them; a renamed field fails here, loudly.
    private static IMessageTypeNaming[] NamingStrategies() =>
        (
            typeof(WolverineMessageNaming)
                .GetField("_namingStrategies", BindingFlags.Static | BindingFlags.NonPublic)
                ?.GetValue(null) as IEnumerable<IMessageTypeNaming>
            ?? throw new InvalidOperationException("Wolverine no longer keeps _namingStrategies.")
        ).ToArray();
}
