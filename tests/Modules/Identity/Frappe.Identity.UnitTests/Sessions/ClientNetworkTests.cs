using System.Net;
using Frappe.Identity.Application.Sessions;

namespace Frappe.Identity.UnitTests.Sessions;

/// <summary>A session keeps only the IPv4 /24 or the IPv6 /48 of its client (S13, data minimization).</summary>
public sealed class ClientNetworkTests
{
    [Test]
    public async Task An_ipv4_address_is_cut_to_its_slash_24()
    {
        // Act
        var prefix = ClientNetwork.PrefixOf(IPAddress.Parse("203.0.113.77"));

        // Assert
        await Assert.That(prefix).IsEqualTo(IPNetwork.Parse("203.0.113.0/24"));
    }

    [Test]
    public async Task An_ipv6_address_is_cut_to_its_slash_48()
    {
        // Act
        var prefix = ClientNetwork.PrefixOf(IPAddress.Parse("2001:db8:abcd:1234:5678:9abc:def0:1234"));

        // Assert
        await Assert.That(prefix).IsEqualTo(IPNetwork.Parse("2001:db8:abcd::/48"));
    }

    [Test]
    public async Task An_ipv4_address_in_ipv6_form_counts_as_ipv4()
    {
        // Act
        var prefix = ClientNetwork.PrefixOf(IPAddress.Parse("::ffff:203.0.113.77"));

        // Assert
        await Assert.That(prefix).IsEqualTo(IPNetwork.Parse("203.0.113.0/24"));
    }

    [Test]
    public async Task No_address_keeps_no_prefix()
    {
        // Act
        var prefix = ClientNetwork.PrefixOf(null);

        // Assert
        await Assert.That(prefix).IsNull();
    }
}
