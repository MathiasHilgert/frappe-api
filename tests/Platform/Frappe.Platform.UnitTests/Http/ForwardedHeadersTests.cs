using Frappe.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Frappe.Platform.UnitTests.Http;

/// <summary>Who a client is behind a proxy: the forwarded address is believed only from a proxy the host trusts.</summary>
public sealed class ForwardedHeadersTests
{
    private const string Proxy = "10.0.0.5";

    private static async Task<WebApplication> StartAsync(string? proxies, string? networks = null)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Http:ForwardedHeaders:KnownProxies"] = proxies,
            ["Http:ForwardedHeaders:KnownNetworks"] = networks,
        });
        builder.Services.AddSingleton<IRateLimitStore, AllowAllRateLimitStore>().AddFrappeHttp();
        var app = builder.Build();

        // The connection's peer: the test server has none of its own.
        app.Use((context, next) =>
        {
            context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(Proxy);
            return next(context);
        });
        app.UseFrappeHttp();
        app.MapGet("/who", (HttpContext context) => $"{ClientAddress.Of(context)} {context.Request.Scheme}");
        await app.StartAsync();
        return app;
    }

    private static async Task<string> WhoAsync(WebApplication app)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/who");
        request.Headers.Add("X-Forwarded-For", "203.0.113.9");
        request.Headers.Add("X-Forwarded-Proto", "https");
        using var response = await app.GetTestClient().SendAsync(request);
        return await response.Content.ReadAsStringAsync();
    }

    [Test]
    public async Task With_no_trusted_proxy_the_forwarded_headers_are_ignored_so_a_client_cannot_spoof_its_address()
    {
        // Arrange
        await using var app = await StartAsync(proxies: null);

        // Act
        var who = await WhoAsync(app);

        // Assert
        await Assert.That(who).IsEqualTo($"{Proxy} http");
    }

    [Test]
    public async Task A_trusted_proxy_by_address_or_by_network_hands_over_the_client_and_the_scheme()
    {
        // Arrange
        await using var byAddress = await StartAsync(proxies: $"{Proxy}, 10.0.0.6");
        await using var byNetwork = await StartAsync(proxies: null, networks: "10.0.0.0/24");

        // Act
        var address = await WhoAsync(byAddress);
        var network = await WhoAsync(byNetwork);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(address).IsEqualTo("203.0.113.9 https");
            await Assert.That(network).IsEqualTo("203.0.113.9 https");
        }
    }

    [Test]
    public async Task A_proxy_that_is_not_trusted_keeps_its_own_address()
    {
        // Arrange
        await using var app = await StartAsync(proxies: "10.0.0.99");

        // Act
        var who = await WhoAsync(app);

        // Assert
        await Assert.That(who).IsEqualTo($"{Proxy} http");
    }

    [Test]
    public async Task An_entry_that_is_not_an_address_fails_at_startup_naming_the_key_and_the_entry()
    {
        // Act
        var failure = await Assert.ThrowsAsync<OptionsValidationException>(async () => await StartAsync(proxies: "proxy.internal"));

        // Assert
        await Assert.That(failure!.Message).Contains("Configuration 'Http:ForwardedHeaders:KnownProxies' has an entry that is not an IP address: "
            + "'proxy.internal'.");
    }
}
