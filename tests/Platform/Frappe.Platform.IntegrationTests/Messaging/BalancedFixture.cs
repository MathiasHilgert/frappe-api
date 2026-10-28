using Microsoft.Extensions.Hosting;

namespace Frappe.Platform.IntegrationTests.Messaging;

// The fixture host as deployed: Production, so Wolverine runs Balanced, on the app role.
public sealed class BalancedFixture : MessagingFixture
{
    protected override string HostEnvironment => Environments.Production;
}
