using Frappe.Platform.IntegrationTests.Persistence;
using Frappe.TestUtilities;

namespace Frappe.Platform.IntegrationTests.Messaging;

// A database of its own for tests that start their hosts, often several per test: an api and its workers
// share it, as deployed, and every test takes a fresh NatsScope on the assembly's NATS server.
public sealed class NatsFixture(AppFixture app) : MessagingFixture(app)
{
    public FrappeNats Server => SharedNats;

    protected override string? HostEnvironment => null;
}
