using System.Net;
using Frappe.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Frappe.Platform.UnitTests.Http;

/// <summary>The proxies trusted to forward the client's address and scheme (<c>Http:ForwardedHeaders</c>).</summary>
public sealed class TrustedProxyTests
{
    private const string Proxies = "Http:ForwardedHeaders:KnownProxies";
    private const string Networks = "Http:ForwardedHeaders:KnownNetworks";

    private static async Task<WebApplication> StartAsync(string? proxies, string? networks)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { [Proxies] = proxies, [Networks] = networks });
        builder.Services.AddFrappeHttp();
        var app = builder.Build();
        app.UseFrappeHttp();
        app.MapGet("/who", (HttpContext context) => $"{context.Connection.RemoteIpAddress}|{context.Request.Scheme}");
        await app.StartAsync();
        return app;
    }

    /// <summary>Asks <c>/who</c> as if the connection came from <paramref name="from"/>, with the forwarded headers a proxy adds.</summary>
    private static async Task<string> WhoAsync(WebApplication app, string from)
    {
        var context = await app.GetTestServer().SendAsync(http =>
        {
            http.Request.Method = HttpMethods.Get;
            http.Request.Path = "/who";
            http.Connection.RemoteIpAddress = IPAddress.Parse(from);
            http.Request.Headers["X-Forwarded-For"] = "203.0.113.7";
            http.Request.Headers["X-Forwarded-Proto"] = "https";
        });
        using var reader = new StreamReader(context.Response.Body);
        return await reader.ReadToEndAsync();
    }

    [Test]
    public async Task With_no_proxy_configured_the_forwarded_headers_are_ignored()
    {
        // Arrange
        await using var app = await StartAsync(null, null);

        // Act
        var who = await WhoAsync(app, "10.0.0.5");

        // Assert
        await Assert.That(who).IsEqualTo("10.0.0.5|http");
    }

    [Test]
    public async Task A_trusted_proxy_address_gets_its_forwarded_headers_believed()
    {
        // Arrange
        await using var app = await StartAsync("10.0.0.5, 10.0.0.6", null);

        // Act
        var trusted = await WhoAsync(app, "10.0.0.5");
        var stranger = await WhoAsync(app, "10.0.0.9");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(trusted).IsEqualTo("203.0.113.7|https");
            await Assert.That(stranger).IsEqualTo("10.0.0.9|http");
        }
    }

    [Test]
    public async Task A_trusted_network_gets_its_forwarded_headers_believed()
    {
        // Arrange
        await using var app = await StartAsync(null, "10.0.0.0/8");

        // Act
        var inside = await WhoAsync(app, "10.1.2.3");
        var outside = await WhoAsync(app, "192.168.0.1");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(inside).IsEqualTo("203.0.113.7|https");
            await Assert.That(outside).IsEqualTo("192.168.0.1|http");
        }
    }

    [Test]
    [Arguments(Proxies, "10.0.0.5, not-an-address", "Configuration 'Http:ForwardedHeaders:KnownProxies' has an invalid entry 'not-an-address'.")]
    [Arguments(Networks, "10.0.0.0/8, 10.0.0.0/99", "Configuration 'Http:ForwardedHeaders:KnownNetworks' has an invalid entry '10.0.0.0/99'.")]
    public async Task An_entry_that_does_not_parse_fails_at_startup_naming_the_key_and_the_entry(string key, string value, string message)
    {
        // Arrange
        var proxies = key == Proxies ? value : null;
        var networks = key == Networks ? value : null;

        // Act
        var failure = await Assert.ThrowsAsync<OptionsValidationException>(async () => await StartAsync(proxies, networks));

        // Assert
        await Assert.That(failure!.Message).Contains(message);
    }

    [Test]
    public async Task Entries_are_comma_separated_and_trimmed()
    {
        // Act
        var options = new TrustedProxyOptions { KnownProxies = " 10.0.0.5 ,, ::1 ", KnownNetworks = "10.0.0.0/8" };

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(options.Proxies().Select(p => p.ToString())).IsEquivalentTo(["10.0.0.5", "::1"]);
            await Assert.That(options.Networks()).Count().IsEqualTo(1);
            await Assert.That(new TrustedProxyOptions().Proxies()).IsEmpty();
        }
    }
}
