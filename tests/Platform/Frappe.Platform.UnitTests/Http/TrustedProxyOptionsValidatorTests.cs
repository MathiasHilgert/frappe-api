using Frappe.Http.TrustedProxies;

namespace Frappe.Platform.UnitTests.Http;

/// <summary>The trusted proxies are checked at startup, every failure naming its full key and the entry (CFG-03).</summary>
public sealed class TrustedProxyOptionsValidatorTests
{
    private readonly TrustedProxyOptionsValidator _validator = new();

    [Test]
    public async Task An_address_and_a_network_that_do_not_parse_are_both_reported_naming_the_key_and_the_entry()
    {
        // Act
        var result = _validator.Validate(null, new TrustedProxyOptions
        {
            KnownProxies = "10.0.0.5, proxy.internal",
            KnownNetworks = "10.0.0.0/8,10.0.0.0/99"
        });

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.Failed).IsTrue();
            await Assert.That(result.Failures!.Count()).IsEqualTo(2);
            await Assert.That(result.FailureMessage).Contains("Configuration 'Http:ForwardedHeaders:KnownProxies' has an entry that is not an IP "
                + "address: 'proxy.internal'.");
            await Assert.That(result.FailureMessage).Contains("Configuration 'Http:ForwardedHeaders:KnownNetworks' has an entry that is not a "
                + "network in CIDR notation: '10.0.0.0/99'.");
        }
    }

    [Test]
    public async Task Nothing_configured_and_valid_lists_pass_and_parse_into_addresses_and_networks()
    {
        // Arrange
        var options = new TrustedProxyOptions { KnownProxies = " 10.0.0.5 , ::1 ", KnownNetworks = "172.16.0.0/12" };

        // Act
        var empty = _validator.Validate(null, new TrustedProxyOptions());
        var valid = _validator.Validate(null, options);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(empty.Succeeded).IsTrue();
            await Assert.That(valid.Succeeded).IsTrue();
            await Assert.That(options.Proxies().Select(proxy => proxy.ToString())).IsEquivalentTo(["10.0.0.5", "::1"]);
            await Assert.That(options.Networks().Select(network => network.ToString())).IsEquivalentTo(["172.16.0.0/12"]);
        }
    }
}
