namespace Frappe.Platform.IntegrationTests.Messaging;

// Stops a NATS server of its own, so it runs alone, never next to a test that needs NATS up.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class NatsOutageGroup : ICollectionFixture<NatsOutageFixture>
{
    public const string Name = "NATS outage";
}
