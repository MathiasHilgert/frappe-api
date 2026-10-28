using System.Reflection;
using Frappe.Platform.Messaging;
using Frappe.Platform.Persistence;
using Microsoft.Extensions.Hosting;
using Wolverine.Util;

namespace Frappe.Platform.IntegrationTests.Messaging;

// Registration only: no host starts, so no database is needed.
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
