using System.Net;
using Frappe.Platform.Web.Forwarding;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Shouldly;

namespace Frappe.IntegrationTests;

/// <summary>X-Forwarded-For is the client address only when a configured proxy sent it.</summary>
public sealed class ForwardedHeadersTests
{
    private static readonly IPAddress Proxy = IPAddress.Parse("10.1.2.3");

    [Fact]
    public async Task AKnownProxy_ForwardsTheClientAddress() =>
        (await ClientIpAsync(Proxy, "203.0.113.7", ("KnownProxies:0", "10.1.2.3"))).ShouldBe("203.0.113.7");

    [Fact]
    public async Task AProxyInsideAKnownNetwork_ForwardsTheClientAddress() =>
        (await ClientIpAsync(Proxy, "203.0.113.7", ("KnownNetworks:0", "10.1.0.0/16"))).ShouldBe("203.0.113.7");

    [Fact]
    public async Task AnUnknownSender_CannotSpoofItsAddress() =>
        (await ClientIpAsync(Proxy, "203.0.113.7")).ShouldBe("10.1.2.3");

    [Fact]
    public async Task NoTransportAddress_IsUnknown() =>
        (await ClientIpAsync(null, null)).ShouldBe("unknown");

    private static async Task<string> ClientIpAsync(IPAddress? remote, string? forwardedFor, params (string Key, string Value)[] settings)
    {
        await using var app = await StartAsync(settings);
        var context = await app.GetTestServer().SendAsync(c =>
        {
            c.Request.Path = "/ip";
            c.Connection.RemoteIpAddress = remote;
            if (forwardedFor is not null)
                c.Request.Headers["X-Forwarded-For"] = forwardedFor;
        }, TestContext.Current.CancellationToken);

        using var reader = new StreamReader(context.Response.Body);
        return await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
    }

    // The same two calls the API's composition root makes, around an endpoint that echoes the client address.
    private static async Task<WebApplication> StartAsync((string Key, string Value)[] settings)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s =>
                new KeyValuePair<string, string?>($"{TrustedProxyExtensions.Section}:{s.Key}", s.Value)))
            .Build();
        builder.Services.AddTrustedForwardedHeaders(configuration);

        var app = builder.Build();
        app.UseTrustedForwardedHeaders();
        app.MapGet("/ip", (HttpContext http) => http.ClientIp());
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }
}
