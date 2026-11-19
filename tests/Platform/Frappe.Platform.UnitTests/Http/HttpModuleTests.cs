using Frappe.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Frappe.Platform.UnitTests.Http;

/// <summary>The Api facet of a module: the host composes its services and maps its endpoints through one interface.</summary>
public sealed class HttpModuleTests
{
    /// <summary>The service the orders module registers.</summary>
    private sealed record OrdersGreeting(string Text);

    /// <summary>A module with one service and one endpoint that reads it.</summary>
    private sealed class OrdersHttp : IHttpModule
    {
        public void AddServices(IServiceCollection services) => services.AddSingleton(new OrdersGreeting("orders-greeting"));

        public void MapEndpoints(IEndpointRouteBuilder v1) => v1.MapGet("/orders", (OrdersGreeting greeting) => greeting.Text);
    }

    [Test]
    public async Task A_listed_module_contributes_its_services_and_endpoints_to_the_host()
    {
        // Arrange
        IHttpModule module = new OrdersHttp();
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        module.AddServices(builder.Services);
        var app = builder.Build();
        module.MapEndpoints(app.MapGroup("/v1").NegotiateRequestLanguage());
        await app.StartAsync();

        // Act
        var response = await app.GetTestClient().GetAsync("/v1/orders");
        var body = await response.Content.ReadAsStringAsync();
        await app.StopAsync();

        // Assert
        await Assert.That((int)response.StatusCode).IsEqualTo(StatusCodes.Status200OK);
        await Assert.That(body).IsEqualTo("orders-greeting");
    }
}
