using System.Net;
using System.Text.Json;
using Frappe.Http;
using Frappe.Platform.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Frappe.Platform.UnitTests.Http;

/// <summary>A host lists its HTTP modules once: their services are registered in order and their endpoints share <c>/v1</c>'s conventions.</summary>
public sealed class HttpModulesTests
{
    /// <summary>What a module registers, to see which ran and in what order.</summary>
    private sealed class Marker(string name)
    {
        public string Name { get; } = name;
    }

    /// <summary>A module that registers a marker and maps one endpoint.</summary>
    private sealed class OrdersModule(string name, string route) : IHttpModule
    {
        public void AddServices(IServiceCollection services) => services.AddSingleton(new Marker(name));

        public void MapEndpoints(IEndpointRouteBuilder v1) => v1.MapGet(route, () => name);
    }

    private static async Task<WebApplication> StartAsync(params IHttpModule[] modules)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services
            .AddFrappeHttp(new AnonymousRateLimit(2, TimeSpan.FromMinutes(10)))
            .AddErrorMessages(PlatformTestMessages.Assembly)
            .AddHttpModules(modules);
        var app = builder.Build();
        app.UseFrappeHttp();
        app.MapHttpModules(modules);
        await app.StartAsync();
        return app;
    }

    [Test]
    public async Task Every_module_registers_its_services_in_the_order_listed_and_maps_its_endpoints_under_v1()
    {
        // Arrange
        await using var app = await StartAsync(new OrdersModule("orders", "/orders"), new OrdersModule("invoices", "/invoices"));
        var client = app.GetTestClient();

        // Act
        var orders = await client.GetStringAsync("/v1/orders");
        var invoices = await client.GetStringAsync("/v1/invoices");
        using var outside = await client.GetAsync("/orders");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(app.Services.GetServices<Marker>().Select(marker => marker.Name)).IsEquivalentTo(["orders", "invoices"]);
            await Assert.That(orders).IsEqualTo("orders");
            await Assert.That(invoices).IsEqualTo("invoices");
            await Assert.That(outside.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        }
    }

    [Test]
    public async Task The_endpoints_answer_head_and_share_the_anonymous_rate_limit_with_a_declared_429()
    {
        // Arrange
        await using var app = await StartAsync(new OrdersModule("orders", "/orders"));
        var client = app.GetTestClient();
        using var head = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, "/v1/orders"));
        using var second = await client.GetAsync("/v1/orders");

        // Act
        using var limited = await client.GetAsync("/v1/orders");
        using var problem = JsonDocument.Parse(await limited.Content.ReadAsStringAsync());
        var endpoint = app.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().First(e =>
            e.RoutePattern.RawText == "/v1/orders");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(head.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(second.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(limited.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
            await Assert.That(problem.RootElement.GetProperty("errors")[0].GetProperty("code").GetString())
                .IsEqualTo(HttpRegistration.RateLimitedCode);
            await Assert.That(endpoint.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>().Any(produces =>
                produces.StatusCode == StatusCodes.Status429TooManyRequests)).IsTrue();
        }
    }
}
