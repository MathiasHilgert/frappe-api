using System.Net;
using Frappe.Http;
using Microsoft.AspNetCore.Http;

namespace Frappe.Platform.UnitTests.Http;

/// <summary>The client a request counts against, the same for every per-client limit.</summary>
public sealed class ClientAddressTests
{
    [Test]
    public async Task A_request_counts_against_its_remote_address_or_one_unknown_client_without_one()
    {
        // Arrange
        var known = new DefaultHttpContext();

        // Act
        known.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(ClientAddress.Of(known)).IsEqualTo("203.0.113.7");
            await Assert.That(ClientAddress.Of(new DefaultHttpContext())).IsEqualTo("unknown");
            await Assert.That(ClientAddress.Unknown).IsEqualTo("unknown");
        }
    }
}
