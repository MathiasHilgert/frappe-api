using Alba;
using Frappe.Platform.IntegrationTests.Modularity;
using Frappe.Platform.IntegrationTests.Persistence;
using Frappe.TestUtilities;

namespace Frappe.Platform.IntegrationTests.Messaging;

// A module's integration tests run on a database alone: the test host must start without a NATS server, even though
// Wolverine keeps its request/reply endpoint on the disabled transport.
public sealed class TestHostWithoutNatsTests(AppFixture fixture)
{
    [Fact]
    public async Task A_test_host_without_nats_starts_and_serves_a_request()
    {
        await using IAlbaHost host = await FrappeTestHost.StartAsync(
            fixture.ConnectionString,
            [new PingModule()],
            nats: null
        );

        await host.Scenario(static scenario =>
        {
            scenario.Get.Url("/v1/pings/latest");
            scenario.ContentShouldBe("pong");
        });
    }
}
