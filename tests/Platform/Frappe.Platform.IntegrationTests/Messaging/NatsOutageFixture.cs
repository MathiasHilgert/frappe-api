using Frappe.Platform.IntegrationTests.Persistence;
using Frappe.TestUtilities;

namespace Frappe.Platform.IntegrationTests.Messaging;

// A database and a NATS server whose port survives a restart; the tests start their own hosts.
public sealed class NatsOutageFixture(AppFixture app) : MessagingFixture(app)
{
    public FrappeNats Server { get; } = FrappeNats.WithFixedPort();

    protected override string? HostEnvironment => null;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await Server.StartAsync();
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await Server.DisposeAsync();
    }
}
