using System.Net;
using System.Text.Json;
using Frappe.Http;
using Frappe.Modules;
using Frappe.Notifications.Application.Requests.SendNotification;
using Frappe.Notifications.Http;
using Frappe.Results;
using Frappe.UseCases;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Api.IntegrationTests;

/// <summary>
/// The explicit send route (S1, S2): mapped only while the host runs as Development, 202 with no Location, one request identity per
/// <c>Idempotency-Key</c> and caller, and the use case's refusals answered as Problems with their codes (S16).
/// </summary>
public sealed class NotificationsRouteTests
{
    private const string Route = "/v1/notifications/sends";

    private static readonly object Body = new
    {
        kind = "account_activation_code",
        payload = new { email = "ana@example.com", language = "es-AR", name = "Ana", code = "482913" },
    };

    private static Task<WebApplication> StartAsync(FakeHandler handler, string environment) =>
        ApiHost.StartAsync([new FakeModule(handler)], [new NotificationsHttp()], environment: environment);

    private static HttpRequestMessage Post(string? key = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Route) { Content = JsonContent.Create(Body) };
        if (key is not null)
        {
            request.Headers.Add("Idempotency-Key", key);
        }

        return request;
    }

    [Test]
    public async Task Outside_Development_the_route_is_not_mapped_and_the_contract_does_not_list_it()
    {
        // Arrange
        var handler = new FakeHandler();
        await using var app = await StartAsync(handler, "Production");
        using var client = app.GetTestClient();

        // Act
        using var response = await client.SendAsync(Post());
        using var contract = await client.GetAsync("/openapi/v1.json");
        using var document = JsonDocument.Parse(await contract.Content.ReadAsStringAsync());

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
            await Assert.That(handler.Received).IsEmpty();
            await Assert.That(document.RootElement.GetProperty("paths").TryGetProperty(Route, out _)).IsFalse();
        }
    }

    [Test]
    public async Task In_Development_the_route_answers_202_without_a_Location_and_hands_the_kind_and_payload_to_the_use_case()
    {
        // Arrange
        var handler = new FakeHandler();
        await using var app = await StartAsync(handler, "Development");
        using var client = app.GetTestClient();

        // Act
        using var response = await client.SendAsync(Post("key-1"));

        // Assert
        var command = handler.Received.Single();
        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
            await Assert.That(response.Headers.Location).IsNull();
            await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
            await Assert.That(command.Kind).IsEqualTo("account_activation_code");
            await Assert.That(command.Payload.GetProperty("code").GetString()).IsEqualTo("482913");
        }
    }

    [Test]
    public async Task The_same_key_gives_the_same_request_identity_and_another_key_or_none_gives_another()
    {
        // Arrange
        var handler = new FakeHandler();
        await using var app = await StartAsync(handler, "Development");
        using var client = app.GetTestClient();

        // Act
        foreach (var key in new string?[] { "key-1", "key-1", "key-2", null, null })
        {
            using var response = await client.SendAsync(Post(key));
            response.EnsureSuccessStatusCode();
        }

        // Assert
        var ids = handler.Received.Select(command => command.RequestId).ToArray();
        using (Assert.Multiple())
        {
            await Assert.That(ids[0]).IsEqualTo(ids[1]);
            await Assert.That(ids[2]).IsNotEqualTo(ids[0]);
            await Assert.That(ids.Distinct().Count()).IsEqualTo(4);
            await Assert.That(ids[0].ToString()[14]).IsEqualTo('5');
        }
    }

    [Test]
    public async Task An_unknown_kind_is_a_problem_with_its_code_its_parameter_and_a_message()
    {
        // Arrange
        var handler = new FakeHandler { Answer = Error.InvalidValue("notifications.kind.unknown", ("kind", "nope")) };
        await using var app = await StartAsync(handler, "Development");
        using var client = app.GetTestClient();

        // Act
        using var response = await client.SendAsync(Post("key-1"));
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        // Assert
        var error = problem.RootElement.GetProperty("errors")[0];
        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
            await Assert.That(error.GetProperty("code").GetString()).IsEqualTo("notifications.kind.unknown");
            await Assert.That(error.GetProperty("parameters").GetProperty("kind").GetString()).IsEqualTo("nope");
            await Assert.That(error.GetProperty("message").GetString()).Contains("nope");
        }
    }

    [Test]
    public async Task An_invalid_payload_is_a_problem_naming_the_fields()
    {
        // Arrange
        var handler = new FakeHandler
        {
            Answer = Error.InvalidValue("notifications.payload.invalid", ("kind", "account_activation_code"), ("fields", new[] { "email", "code" })),
        };
        await using var app = await StartAsync(handler, "Development");
        using var client = app.GetTestClient();

        // Act
        using var response = await client.SendAsync(Post());
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        // Assert
        var error = problem.RootElement.GetProperty("errors")[0];
        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
            await Assert.That(error.GetProperty("code").GetString()).IsEqualTo("notifications.payload.invalid");
            await Assert.That(error.GetProperty("parameters").GetProperty("fields").EnumerateArray().Select(f => f.GetString()!))
                .IsEquivalentTo(["email", "code"]);
        }
    }

    [Test]
    public async Task With_the_real_module_lists_an_action_url_that_is_not_https_is_answered_as_an_invalid_payload()
    {
        // Arrange
        await using var app = await ApiHost.StartAsync(
            [new OpenStoreModule()], Frappe.Api.Modules.Endpoints, ApiHost.Deployed, "Development");
        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, Route)
        {
            Content = JsonContent.Create(new
            {
                kind = "product_announcement",
                payload = new { email = "ana@example.com", name = "Ana", headline = "News", summary = "More", url = "http://frappe.example/news" },
            }),
        };
        request.Headers.Add("Idempotency-Key", "key-http");

        // Act
        using var response = await client.SendAsync(request);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        // Assert
        var error = problem.RootElement.GetProperty("errors")[0];
        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
            await Assert.That(error.GetProperty("code").GetString()).IsEqualTo("notifications.payload.invalid");
            await Assert.That(error.GetProperty("parameters").GetProperty("fields").EnumerateArray().Select(f => f.GetString()!))
                .IsEquivalentTo(["payload"]);
        }
    }

    [Test]
    public async Task In_Development_the_contract_documents_the_operation_with_its_accepted_answer()
    {
        // Arrange
        await using var app = await StartAsync(new FakeHandler(), "Development");
        using var client = app.GetTestClient();

        // Act
        using var contract = await client.GetAsync("/openapi/v1.json");
        using var document = JsonDocument.Parse(await contract.Content.ReadAsStringAsync());

        // Assert
        var post = document.RootElement.GetProperty("paths").GetProperty(Route).GetProperty("post");
        using (Assert.Multiple())
        {
            await Assert.That(post.GetProperty("operationId").GetString()).IsEqualTo("SendNotification");
            await Assert.That(post.GetProperty("responses").TryGetProperty("202", out _)).IsTrue();
            await Assert.That(post.GetProperty("parameters").EnumerateArray().Select(p => p.GetProperty("name").GetString()))
                .Contains("Idempotency-Key");
        }
    }

    [Test]
    public async Task The_request_identity_is_a_standard_name_based_UUID_version_5()
    {
        // Arrange: the RFC 9562 / Python documentation vector for the DNS namespace and the name python.org.
        var dns = new Guid("6ba7b810-9dad-11d1-80b4-00c04fd430c8");

        // Act
        var identity = Frappe.Notifications.Http.Requests.RequestIdentity.Version5(dns, "python.org");

        // Assert
        await Assert.That(identity).IsEqualTo(new Guid("886313e1-3b8a-5372-9b90-0c9aee199e5d"));
    }

    /// <summary>The use case, standing in for the real one (which needs a database): it keeps what it was asked and answers as told.</summary>
    private sealed class FakeHandler : ICommandHandler<SendNotification, Unit>
    {
        public List<SendNotification> Received { get; } = [];

        public Error? Answer { get; init; }

        public Task<Result<Unit>> HandleAsync(SendNotification command, CancellationToken cancellationToken)
        {
            Received.Add(command);
            return Task.FromResult<Result<Unit>>(Answer is null ? Unit.Value : Answer);
        }
    }

    /// <summary>Registers the fake use case and an idempotency store that never replays, so no Valkey is needed.</summary>
    private sealed class FakeModule(FakeHandler handler) : IModule
    {
        public void AddServices(IServiceCollection services)
        {
            services.AddSingleton<ICommandHandler<SendNotification, Unit>>(handler);
            services.AddSingleton<IIdempotencyStore, OpenStore>();
        }
    }

    /// <summary>Replaces only the idempotency store (it needs Valkey), leaving the real handler and its checks in place.</summary>
    private sealed class OpenStoreModule : IModule
    {
        public void AddServices(IServiceCollection services) => services.AddSingleton<IIdempotencyStore, OpenStore>();
    }

    /// <summary>An idempotency store that claims every key and replays nothing.</summary>
    private sealed class OpenStore : IIdempotencyStore
    {
        public ValueTask<IdempotencyClaim> ClaimAsync(string key, string fingerprint, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new IdempotencyClaim(IdempotencyOutcome.Claimed));

        public ValueTask CompleteAsync(string key, string fingerprint, StoredResponse response, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask ReleaseAsync(string key, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}
