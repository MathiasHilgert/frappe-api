using System.Net;
using System.Text.Json;
using Frappe.Http;
using Frappe.Identity;
using Frappe.Identity.Http;
using Frappe.Identity.Infrastructure;
using Frappe.Identity.Limits.Valkey;
using Frappe.Modules;
using Frappe.Notifications;
using Frappe.Notifications.Channels.Email;
using Frappe.Notifications.Http;
using Frappe.Notifications.Infrastructure;
using Frappe.Notifications.Kinds.Account;
using Frappe.Notifications.Kinds.Marketing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Frappe.Api.IntegrationTests;

/// <summary>The Api as deployed: it starts with zero modules, answers its probes and its contract, and composes the modules it is given.</summary>
public sealed class ApiHostTests
{
    [Test]
    public async Task The_Api_starts_with_zero_modules_and_answers_its_probes()
    {
        // Arrange
        await using var app = await ApiHost.StartAsync();
        using var client = app.GetTestClient();

        // Act
        using var live = await client.GetAsync("/health/live");
        using var startup = await client.GetAsync("/health/startup");
        using var ready = await client.GetAsync("/health/ready");
        var liveChecks = await CheckNamesAsync(live);
        var startupChecks = await CheckNamesAsync(startup);
        var readyChecks = await CheckNamesAsync(ready);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(live.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(startup.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(ready.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(liveChecks).Contains("process");
            await Assert.That(startupChecks).Contains("started");
            await Assert.That(readyChecks).Contains("valkey");
        }
    }

    [Test]
    public async Task With_zero_modules_an_unknown_route_answers_a_problem_with_a_title_and_a_detail()
    {
        // Arrange
        await using var app = await ApiHost.StartAsync();
        using var client = app.GetTestClient();

        // Act
        using var response = await client.GetAsync("/v1/planets");
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
            await Assert.That(problem.RootElement.GetProperty("title").GetString()).IsNotNullOrWhiteSpace();
            await Assert.That(problem.RootElement.GetProperty("detail").GetString()).IsNotNullOrWhiteSpace();
        }
    }

    [Test]
    public async Task The_OpenAPI_document_carries_the_Api_options_and_the_reference_is_served()
    {
        // Arrange
        await using var app = await ApiHost.StartAsync();
        using var client = app.GetTestClient();

        // Act
        using var response = await client.GetAsync("/openapi/v1.json");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var info = document.RootElement.GetProperty("info");
        using var reference = await client.GetAsync("/docs/");
        var referencePage = await reference.Content.ReadAsStringAsync();

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(info.GetProperty("title").GetString()).IsEqualTo("Frappé API");
            await Assert.That(info.GetProperty("version").GetString()).IsEqualTo("1.0.0");
            await Assert.That(info.GetProperty("description").GetString()).Contains("/v1");
            await Assert.That(reference.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(referencePage).Contains("Frappé API");
        }
    }

    [Test]
    public async Task An_unknown_route_answers_a_not_found_problem()
    {
        // Arrange
        await using var app = await ApiHost.StartAsync();

        // Act
        using var response = await app.GetTestClient().GetAsync("/v1/nothing-here");
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
            await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("application/problem+json");
            await Assert.That(problem.RootElement.GetProperty("type").GetString()).IsEqualTo("urn:frappe:problem:not-found");
        }
    }

    [Test]
    public async Task A_listed_module_contributes_its_services_and_its_endpoints_under_v1()
    {
        // Arrange
        var marker = new Marker(1);
        await using var app = await ApiHost.StartAsync([new MarkingModule(marker)], [new PingModule()]);
        using var client = app.GetTestClient();

        // Act
        using var response = await client.GetAsync("/v1/ping");
        var body = await response.Content.ReadAsStringAsync();
        using var contract = await client.GetAsync("/openapi/v1.json");
        using var document = JsonDocument.Parse(await contract.Content.ReadAsStringAsync());

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(body).IsEqualTo("pong");
            await Assert.That(app.Services.GetRequiredService<Marker>()).IsSameReferenceAs(marker);
            await Assert.That(document.RootElement.GetProperty("paths").TryGetProperty("/v1/ping", out _)).IsTrue();
        }
    }

    [Test]
    public async Task The_Api_starts_with_its_real_module_lists_when_the_database_is_configured()
    {
        // Arrange
        var settings = new Dictionary<string, string?>(ApiHost.Deployed);

        // Act
        await using var app = await ApiHost.StartAsync([], Modules.Endpoints, settings);
        using var client = app.GetTestClient();
        using var live = await client.GetAsync("/health/live");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(Modules.Endpoints).IsEquivalentTo(
                [Frappe.Access.AccessModule.Api, IdentityModule.Api, NotificationsModule.Api], TUnit.Assertions.Enums.CollectionOrdering.Matching);
            await Assert.That(live.StatusCode).IsEqualTo(HttpStatusCode.OK);
        }
    }

    [Test]
    public async Task The_Api_with_its_real_module_lists_and_no_notifications_connection_fails_the_start_naming_its_key()
    {
        // Arrange
        var settings = new Dictionary<string, string?>(ApiHost.Deployed);
        settings.Remove("ConnectionStrings:Notifications");

        // Act
        var thrown = await Assert.ThrowsAsync<OptionsValidationException>(() => ApiHost.StartAsync([], Modules.Endpoints, settings));

        // Assert
        await Assert.That(thrown?.Message).Contains("ConnectionStrings:Notifications");
    }

    [Test]
    public async Task The_Api_with_its_real_module_lists_and_no_identity_connection_fails_the_start_naming_its_key()
    {
        // Arrange
        var settings = new Dictionary<string, string?>(ApiHost.Deployed);
        settings.Remove("ConnectionStrings:Identity");

        // Act
        var thrown = await Assert.ThrowsAsync<OptionsValidationException>(() => ApiHost.StartAsync([], Modules.Endpoints, settings));

        // Assert
        await Assert.That(thrown?.Message).Contains("ConnectionStrings:Identity");
    }

    [Test]
    public async Task The_Api_with_its_real_module_lists_and_no_address_hash_key_fails_the_start_naming_its_key()
    {
        // Arrange
        var settings = new Dictionary<string, string?>(ApiHost.Deployed);
        settings.Remove("Notifications:AddressHash:Keys:1");

        // Act
        var thrown = await Assert.ThrowsAsync<OptionsValidationException>(() => ApiHost.StartAsync([], Modules.Endpoints, settings));

        // Assert
        await Assert.That(thrown?.Message).Contains("Notifications:AddressHash:CurrentKeyId");
    }

    [Test]
    public async Task The_Api_with_its_real_module_lists_and_no_code_hash_key_fails_the_start_naming_its_key()
    {
        // Arrange
        var settings = new Dictionary<string, string?>(ApiHost.Deployed);
        settings.Remove("Identity:Codes:Key");

        // Act
        var thrown = await Assert.ThrowsAsync<OptionsValidationException>(() => ApiHost.StartAsync([], Modules.Endpoints, settings));

        // Assert
        await Assert.That(thrown?.Message).Contains("Identity:Codes:Key");
    }

    [Test]
    public async Task The_Api_with_its_real_module_lists_gates_every_handler_and_takes_its_caller_from_the_session()
    {
        // Arrange
        await using var app = await ApiHost.StartAsync([], Modules.Endpoints, new Dictionary<string, string?>(ApiHost.Deployed));
        using var scope = app.Services.CreateScope();

        // Act
        var callers = scope.ServiceProvider.GetServices<Frappe.Access.Contracts.ICaller>().ToArray();
        var gate = scope.ServiceProvider.GetRequiredService<Frappe.UseCases.IUseCaseGate>();

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(callers.Select(caller => caller.GetType().Name)).IsEquivalentTo(["SessionCaller"]);
            await Assert.That(callers[0].PrincipalId).IsNull();
            await Assert.That(gate.GetType().Name).IsEqualTo("AccessGate");
        }
    }

    [Test]
    public async Task The_Api_with_its_real_module_lists_composes_the_code_services_over_the_valkey_limiter()
    {
        // Arrange
        await using var app = await ApiHost.StartAsync([], Modules.Endpoints, new Dictionary<string, string?>(ApiHost.Deployed));
        using var scope = app.Services.CreateScope();

        // Act
        var services = (
            scope.ServiceProvider.GetRequiredService<Frappe.Identity.Application.Codes.CodeIssuer>(),
            scope.ServiceProvider.GetRequiredService<Frappe.Identity.Application.Codes.CodeRedeemer>(),
            app.Services.GetRequiredService<Frappe.Identity.Application.Limits.IAttemptLimiter>().GetType().Name);

        // Assert
        await Assert.That(services.Item3).IsEqualTo("ValkeyAttemptLimiter");
    }

    [Test]
    public async Task The_Api_with_its_real_module_lists_and_no_webhook_secret_fails_the_start_naming_its_key()
    {
        // Arrange
        var settings = new Dictionary<string, string?>(ApiHost.Deployed);
        settings.Remove("Notifications:Email:Resend:WebhookSecret");

        // Act
        var thrown = await Assert.ThrowsAsync<OptionsValidationException>(() => ApiHost.StartAsync([], Modules.Endpoints, settings));

        // Assert
        await Assert.That(thrown?.Message).Contains("Notifications:Email:Resend:WebhookSecret");
    }

    [Test]
    public async Task The_Api_with_its_real_module_lists_and_no_unsubscribe_signing_key_fails_the_start_naming_its_key()
    {
        // Arrange
        var settings = new Dictionary<string, string?>(ApiHost.Deployed);
        settings.Remove("Notifications:Unsubscribe:SigningKey");

        // Act
        var thrown = await Assert.ThrowsAsync<OptionsValidationException>(() => ApiHost.StartAsync([], Modules.Endpoints, settings));

        // Assert
        await Assert.That(thrown?.Message).Contains("Notifications:Unsubscribe:SigningKey");
    }

    [Test]
    public async Task The_Api_registers_what_it_registered_when_it_listed_the_identity_facets_itself()
    {
        // Arrange
        // Before the door (S1) the Api listed Identity's facets first among its services and IdentityHttp among its endpoints.
        IModule[] facets = [new IdentityCoreServices(), new IdentityApiServices(), new IdentityLimitsValkeyModule()];
        IHttpModule[] http = [.. Modules.Endpoints.Select(module => module == IdentityModule.Api ? new IdentityHttp() : module)];
        var listed = Registrations(facets, http);

        // Act
        var composed = Registrations([], Modules.Endpoints);

        // Assert
        // Per service, in registration order, the implementation types match. Factories and instances are compared only by kind, so
        // their order across modules (for example health-check registrations) is not checked.
        using (Assert.Multiple())
        {
            await Assert.That(composed.Keys).IsEquivalentTo(listed.Keys);
            foreach (var (service, providers) in listed)
            {
                await Assert.That(composed[service]).IsEquivalentTo(providers, TUnit.Assertions.Enums.CollectionOrdering.Matching)
                    .Because(service);
            }
        }
    }

    [Test]
    public async Task The_Api_registers_what_it_registered_when_it_listed_the_notifications_facets_itself()
    {
        // Arrange
        // Before the door (S1) the Api listed Notifications' shared services, kinds and email webhooks first among its services, and
        // NotificationsHttp last among its endpoints. One entry per module moves those services after Access and Identity.
        IModule[] facets = [new NotificationsCoreServices(), new AccountKindsModule(), new MarketingKindsModule(), new EmailChannelApiModule()];
        IHttpModule[] http = [.. Modules.Endpoints.Select(module => module == NotificationsModule.Api ? new NotificationsHttp() : module)];
        var listed = Registrations(facets, http);

        // Act
        var composed = Registrations(builder => builder.AddFrappeApi());
        var reordered = listed.Keys
            .Where(service => !composed[service].SequenceEqual(listed[service], StringComparer.Ordinal))
            .ToArray();

        // Assert
        // Per service: the same providers, in the same order. Only the hosted services, which are enumerated and never resolved one by
        // one, start in another order: Access's startup check now starts before the Notifications catalog check, as in the Worker.
        using (Assert.Multiple())
        {
            await Assert.That(composed.Keys).IsEquivalentTo(listed.Keys);
            foreach (var (service, providers) in listed)
            {
                await Assert.That(composed[service]).IsEquivalentTo(providers).Because(service);
            }

            await Assert.That(reordered).IsEquivalentTo(["Microsoft.Extensions.Hosting.IHostedService"]);
        }
    }

    /// <summary>
    /// What the Api registers with <paramref name="modules"/> and <paramref name="endpoints"/>: per service type (and key), what provides it,
    /// in registration order.
    /// </summary>
    private static Dictionary<string, string[]> Registrations(IReadOnlyCollection<IModule> modules, IReadOnlyCollection<IHttpModule> endpoints) =>
        Registrations(builder => builder.AddFrappeApi(modules, endpoints));

    /// <summary>What <paramref name="compose"/> registers in the Api: per service type (and key), what provides it, in registration order.</summary>
    private static Dictionary<string, string[]> Registrations(Action<WebApplicationBuilder> compose)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        compose(builder);
        return builder.Services
            .GroupBy(descriptor => descriptor.IsKeyedService
                ? $"{descriptor.ServiceType.FullName} [{descriptor.ServiceKey}]"
                : descriptor.ServiceType.FullName!)
            .ToDictionary(group => group.Key, group => group.Select(Provider).ToArray(), StringComparer.Ordinal);
    }

    private static string Provider(ServiceDescriptor descriptor) => descriptor.IsKeyedService
        ? descriptor.KeyedImplementationType?.FullName ?? descriptor.KeyedImplementationInstance?.GetType().FullName ?? "factory"
        : descriptor.ImplementationType?.FullName ?? descriptor.ImplementationInstance?.GetType().FullName ?? "factory";

    private static async Task<string?[]> CheckNamesAsync(HttpResponseMessage probe)
    {
        using var report = JsonDocument.Parse(await probe.Content.ReadAsStringAsync());
        return [.. report.RootElement.GetProperty("checks").EnumerateArray().Select(check => check.GetProperty("name").GetString())];
    }

    /// <summary>What a test module registers, so a test can tell its services reached the host.</summary>
    private sealed record Marker(int Id);

    /// <summary>A module that contributes services only.</summary>
    private sealed class MarkingModule(Marker marker) : IModule
    {
        public void AddServices(IServiceCollection services) => services.AddSingleton(marker);
    }

    /// <summary>A module that contributes one endpoint under <c>/v1</c>.</summary>
    private sealed class PingModule : IHttpModule
    {
        public void AddServices(IServiceCollection services)
        {
        }

        public void MapEndpoints(IEndpointRouteBuilder v1) => v1.MapGet("/ping", () => "pong");
    }
}
