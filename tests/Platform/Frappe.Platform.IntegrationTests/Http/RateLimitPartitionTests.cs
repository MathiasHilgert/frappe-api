using System.Net;
using Alba;

namespace Frappe.Platform.IntegrationTests.Http;

// One permit per partition: a second request that shares the first one's partition is rejected.
public sealed class RateLimitPartitionTests
{
    // An IPv6 client usually owns a whole /64, so a limit per address would let it rotate past the limit.
    [Theory]
    [InlineData("2001:db8:0:1::1", "2001:db8:0:1:ffff:ffff:ffff:ffff", 429)]
    [InlineData("2001:db8:0:1::1", "2001:db8:0:2::1", 409)]
    [InlineData("192.0.2.1", "192.0.2.1", 429)]
    [InlineData("192.0.2.1", "192.0.2.2", 409)]
    [InlineData("192.0.2.1", "::ffff:192.0.2.1", 429)]
    [InlineData("::ffff:192.0.2.1", "::ffff:192.0.2.2", 409)]
    public async Task The_public_policy_partitions_ipv6_by_its_64_prefix_and_ipv4_by_address(
        string first,
        string second,
        int secondStatus
    )
    {
        await using IAlbaHost host = await HttpHost.StartAsync(
            new ProbeModule(),
            static builder => builder.Configuration["RateLimiting:PublicPermitLimit"] = "1"
        );
        await RequestFromAsync(host, first, 409);

        await RequestFromAsync(host, second, secondStatus);
    }

    private static Task<IScenarioResult> RequestFromAsync(IAlbaHost host, string ip, int status) =>
        host.Scenario(scenario =>
        {
            scenario.ConfigureHttpContext(context =>
                context.Connection.RemoteIpAddress = IPAddress.Parse(ip)
            );
            scenario.Get.Url("/v1/probes/problems/conflict");
            scenario.StatusCodeShouldBe(status);
        });
}
