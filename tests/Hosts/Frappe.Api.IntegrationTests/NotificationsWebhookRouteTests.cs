using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Frappe.Modules;
using Frappe.Notifications.Application.Feedback;
using Frappe.Notifications.Application.Ports;
using Frappe.Notifications.Application.Requests.RecordProviderFeedback;
using Frappe.Notifications.Channels.Email;
using Frappe.Notifications.Domain.Deliveries;
using Frappe.Notifications.Http;
using Frappe.Results;
using Frappe.UseCases;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Api.IntegrationTests;

/// <summary>
/// The Resend webhook route (S4, S12, S16): anonymous but signed, 400 with its code for a call that is not authentic, 204 for everything
/// else, a rate limit of its own, and its operation in the contract.
/// </summary>
#pragma warning disable S6418 // The secret is a local-only placeholder that protects nothing.
public sealed class NotificationsWebhookRouteTests
{
    private const string Route = "/v1/notifications/webhooks/resend";

    private const string Secret = "whsec_MfKQ9r8GKYqrTwjUPD8ILPZIo2LaLaSw";

    private const string Delivered =
        """{"type":"email.delivered","created_at":"2026-11-24T10:00:00.000Z","data":{"email_id":"re_1","to":["ana@example.com"]}}""";

    private static async Task<WebApplication> StartAsync(FakeHandler handler, Dictionary<string, string?>? extra = null)
    {
        var settings = new Dictionary<string, string?> { ["Notifications:Email:Resend:WebhookSecret"] = Secret };
        foreach (var (key, value) in extra ?? [])
        {
            settings[key] = value;
        }

        return await ApiHost.StartAsync([new FakeModule(handler), new EmailChannelApiModule()], [new NotificationsHttp()], settings);
    }

    private static HttpRequestMessage Signed(string body, string id = "msg_1", long? seconds = null, string? signature = null)
    {
        var timestamp = (seconds ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds()).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var hash = HMACSHA256.HashData(Convert.FromBase64String(Secret["whsec_".Length..]), Encoding.UTF8.GetBytes($"{id}.{timestamp}.{body}"));
        var request = new HttpRequestMessage(HttpMethod.Post, Route) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.Add("svix-id", id);
        request.Headers.Add("svix-timestamp", timestamp);
        request.Headers.Add("svix-signature", signature ?? "v1," + Convert.ToBase64String(hash));
        return request;
    }

    [Test]
    public async Task A_signed_call_is_answered_204_and_hands_the_use_case_what_the_provider_said()
    {
        // Arrange
        var handler = new FakeHandler();
        await using var app = await StartAsync(handler);
        using var client = app.GetTestClient();

        // Act
        using var response = await client.SendAsync(Signed(Delivered, "msg_9"));

        // Assert
        var feedback = handler.Received.Single().Feedback;
        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
            await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
            await Assert.That(feedback.EventId).IsEqualTo("msg_9");
            await Assert.That(feedback.ProviderMessageId).IsEqualTo("re_1");
            await Assert.That(feedback.Feedback).IsEqualTo(DeliveryFeedback.Delivered);
        }
    }

    [Test]
    public async Task A_forged_or_expired_call_is_a_400_with_the_signature_code_and_reaches_nothing()
    {
        // Arrange
        var handler = new FakeHandler();
        await using var app = await StartAsync(handler);
        using var client = app.GetTestClient();
        var expired = DateTimeOffset.UtcNow.AddMinutes(-6).ToUnixTimeSeconds();

        // Act
        using var forged = await client.SendAsync(Signed(Delivered, signature: "v1," + Convert.ToBase64String(new byte[32])));
        using var old = await client.SendAsync(Signed(Delivered, seconds: expired));
        using var unsigned = await client.PostAsync(Route, new StringContent(Delivered, Encoding.UTF8, "application/json"));
        using var problem = JsonDocument.Parse(await forged.Content.ReadAsStringAsync());

        // Assert
        var error = problem.RootElement.GetProperty("errors")[0];
        using (Assert.Multiple())
        {
            await Assert.That(forged.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
            await Assert.That(old.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
            await Assert.That(unsigned.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
            await Assert.That(error.GetProperty("code").GetString()).IsEqualTo("notifications.webhook_signature.invalid");
            await Assert.That(error.GetProperty("message").GetString()).Contains("signature");
            await Assert.That(handler.Received).IsEmpty();
        }
    }

    [Test]
    public async Task An_authentic_event_the_module_does_not_track_is_a_204_that_changes_nothing()
    {
        // Arrange
        var handler = new FakeHandler();
        await using var app = await StartAsync(handler);
        using var client = app.GetTestClient();

        // Act
        using var response = await client.SendAsync(Signed("""{"type":"email.opened","data":{"email_id":"re_1"}}"""));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
            await Assert.That(handler.Received).IsEmpty();
        }
    }

    [Test]
    public async Task A_conflict_with_another_writer_is_answered_409_so_the_provider_redelivers()
    {
        // Arrange
        var handler = new FakeHandler { Answer = new ConflictError("commit.conflict") };
        await using var app = await StartAsync(handler);
        using var client = app.GetTestClient();

        // Act
        using var response = await client.SendAsync(Signed(Delivered));

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
    }

    [Test]
    public async Task The_route_has_a_rate_limit_of_its_own_that_the_section_sets()
    {
        // Arrange
        var handler = new FakeHandler();
        await using var app = await StartAsync(handler, new() { ["Notifications:Webhooks:RateLimit:PermitLimit"] = "2" });
        using var client = app.GetTestClient();

        // Act
        var statuses = new List<HttpStatusCode>();
        for (var call = 0; call < 3; call++)
        {
            using var response = await client.SendAsync(Signed(Delivered, $"msg_{call}"));
            statuses.Add(response.StatusCode);
        }

        // Assert
        await Assert.That(statuses).IsEquivalentTo([HttpStatusCode.NoContent, HttpStatusCode.NoContent, HttpStatusCode.TooManyRequests]);
    }

    [Test]
    public async Task The_contract_documents_the_operation_its_signature_headers_and_its_answers()
    {
        // Arrange
        await using var app = await StartAsync(new FakeHandler());
        using var client = app.GetTestClient();

        // Act
        using var contract = await client.GetAsync("/openapi/v1.json");
        using var document = JsonDocument.Parse(await contract.Content.ReadAsStringAsync());

        // Assert
        var post = document.RootElement.GetProperty("paths").GetProperty(Route).GetProperty("post");
        var responses = post.GetProperty("responses");
        using (Assert.Multiple())
        {
            await Assert.That(post.GetProperty("operationId").GetString()).IsEqualTo("ReceiveResendWebhook");
            await Assert.That(responses.TryGetProperty("204", out _)).IsTrue();
            await Assert.That(responses.TryGetProperty("400", out _)).IsTrue();
            await Assert.That(responses.TryGetProperty("429", out _)).IsTrue();
            await Assert.That(post.GetProperty("parameters").EnumerateArray().Select(p => p.GetProperty("name").GetString()))
                .Contains("svix-id").And.Contains("svix-timestamp").And.Contains("svix-signature");
        }
    }

    /// <summary>The use case, standing in for the real one (which needs a database): it keeps what it was asked and answers as told.</summary>
    private sealed class FakeHandler : ICommandHandler<RecordProviderFeedback, Unit>
    {
        public List<RecordProviderFeedback> Received { get; } = [];

        public Error? Answer { get; init; }

        public Task<Result<Unit>> HandleAsync(RecordProviderFeedback command, CancellationToken cancellationToken)
        {
            Received.Add(command);
            return Task.FromResult<Result<Unit>>(Answer is null ? Unit.Value : Answer);
        }
    }

    /// <summary>The message lock without a database: the lock itself is proven against Postgres by the module's integration tests.</summary>
    private sealed class NoLock : IProviderMessageLock
    {
        public Task<IAsyncDisposable> AcquireAsync(DeliveryProvider provider, string providerMessageId, CancellationToken cancellationToken) =>
            Task.FromResult<IAsyncDisposable>(new Released());

        /// <summary>Nothing to release.</summary>
        private sealed class Released : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    /// <summary>Registers the fake use case and the intake that calls it.</summary>
    private sealed class FakeModule(FakeHandler handler) : IModule
    {
        public void AddServices(IServiceCollection services)
        {
            services.AddSingleton<ICommandHandler<RecordProviderFeedback, Unit>>(handler);
            services.AddSingleton<IProviderMessageLock, NoLock>();
            services.AddSingleton<ProviderFeedbackIntake>();
        }
    }
}
