using System.Net;
using System.Text.Json;
using Frappe.Localization;
using Frappe.Notifications.Application.Channels;
using Frappe.Notifications.Domain.Deliveries;
using Frappe.Notifications.Senders.Email;
using Frappe.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;
using Polly.RateLimiting;
using Polly.Timeout;

namespace Frappe.Notifications.UnitTests.Emails;

/// <summary>
/// The Resend adapter against a fake HTTP handler, one case for every class of answer (S14): a 2xx is sent, a 429, 5xx, timeout or the
/// concurrent-idempotency 409 throws so the message is redelivered, the idempotency-misuse 409 raises an alert and is never recorded as
/// failed, and any other 4xx fails for good with a code.
/// </summary>
public sealed class ResendEmailSenderTests : IDisposable
{
    private const string ApiKey = "re_test_never_printed";

    private readonly ServiceProvider _services = new ServiceCollection().AddMetrics().BuildServiceProvider();
    private readonly CapturedLogs _logs = new();
    private readonly StubHandler _handler = new();

    public void Dispose()
    {
        _services.Dispose();
        _handler.Dispose();
        _logs.Dispose();
    }

    private static RenderedEmail Email(IReadOnlyDictionary<string, string>? headers = null) => new(
        EmailAddress.Create("Ana@Example.com").Value,
        "Tu código 482913",
        "<p>Hola</p>",
        "Hola",
        headers ?? new Dictionary<string, string>(),
        "idem-key-1",
        LanguageTag.Create("es-AR").Value);

    private ResendEmailSender Sender(EmailOptions? options = null)
    {
        var client = new HttpClient(_handler, disposeHandler: false) { BaseAddress = new Uri("https://api.resend.com/") };
        return new ResendEmailSender(
            client,
            Options.Create(options ?? new EmailOptions { Resend = { ApiKey = ApiKey } }),
            new EmailProviderMetrics(_services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>()),
            _logs.For<ResendEmailSender>());
    }

    private static string Error(string name, int status) => $$"""{"statusCode":{{status}},"name":"{{name}}","message":"secret text"}""";

    [Test]
    public async Task A_2xx_sends_the_email_to_the_emails_endpoint_with_its_idempotency_key_and_returns_the_provider_id()
    {
        // Arrange
        _handler.Respond(HttpStatusCode.OK, """{"id":"re_msg_1"}""");
        var headers = new Dictionary<string, string> { ["List-Unsubscribe"] = "<https://x.test/u>" };

        // Act
        var outcome = await Sender(new EmailOptions { From = "hola@frappe.test", Resend = { ApiKey = ApiKey } }).SendAsync(Email(headers), default);

        // Assert
        var (request, body, _) = _handler.Received.Single();
        using var json = JsonDocument.Parse(body);
        using (Assert.Multiple())
        {
            await Assert.That(outcome).IsEqualTo(new ChannelOutcome.Sent(DeliveryProvider.Resend, "re_msg_1", null));
            await Assert.That(request.Method).IsEqualTo(HttpMethod.Post);
            await Assert.That(request.RequestUri!.AbsoluteUri).IsEqualTo("https://api.resend.com/emails");
            await Assert.That(request.Headers.GetValues("Idempotency-Key").Single()).IsEqualTo("idem-key-1");
            await Assert.That(json.RootElement.GetProperty("from").GetString()).IsEqualTo("hola@frappe.test");
            await Assert.That(json.RootElement.GetProperty("to")[0].GetString()).IsEqualTo("ana@example.com");
            await Assert.That(json.RootElement.GetProperty("subject").GetString()).IsEqualTo("Tu código 482913");
            await Assert.That(json.RootElement.GetProperty("html").GetString()).IsEqualTo("<p>Hola</p>");
            await Assert.That(json.RootElement.GetProperty("text").GetString()).IsEqualTo("Hola");
            await Assert.That(json.RootElement.GetProperty("headers").GetProperty("List-Unsubscribe").GetString()).IsEqualTo("<https://x.test/u>");
        }
    }

    [Test]
    public async Task The_email_carries_both_logos_as_inline_attachments_named_by_the_cid_references()
    {
        // Arrange
        _handler.Respond(HttpStatusCode.OK, """{"id":"re_msg_1"}""");

        // Act
        await Sender().SendAsync(Email(), default);

        // Assert
        using var json = JsonDocument.Parse(_handler.Received.Single().Body);
        var attachments = json.RootElement.GetProperty("attachments").EnumerateArray().ToArray();
        using (Assert.Multiple())
        {
            await Assert.That(attachments.Select(a => a.GetProperty("content_id").GetString()!))
                .IsEquivalentTo([EmailInlineImage.LightLogoId, EmailInlineImage.DarkLogoId]);
            await Assert.That(attachments.Select(a => a.GetProperty("filename").GetString()!))
                .IsEquivalentTo(["frappe-logo-light.png", "frappe-logo-dark.png"]);
            await Assert.That(attachments.Select(a => a.GetProperty("content_type").GetString()!).Distinct()).IsEquivalentTo(["image/png"]);
            await Assert.That(attachments.Select(a => Convert.FromBase64String(a.GetProperty("content").GetString()!)[1..4])
                .Select(Convert.ToHexString).Distinct()).IsEquivalentTo(["504E47"]);
        }
    }

    [Test]
    public async Task An_email_without_extra_headers_sends_no_headers_object()
    {
        // Arrange
        _handler.Respond(HttpStatusCode.OK, """{"id":"re_msg_1"}""");

        // Act
        await Sender().SendAsync(Email(), default);

        // Assert
        using var json = JsonDocument.Parse(_handler.Received.Single().Body);
        await Assert.That(json.RootElement.TryGetProperty("headers", out _)).IsFalse();
    }

    [Test]
    [Arguments(HttpStatusCode.TooManyRequests)]
    [Arguments(HttpStatusCode.InternalServerError)]
    [Arguments(HttpStatusCode.BadGateway)]
    [Arguments(HttpStatusCode.ServiceUnavailable)]
    [Arguments(HttpStatusCode.RequestTimeout)]
    public async Task A_429_a_5xx_or_a_408_throws_so_the_message_is_redelivered_and_is_never_failed(HttpStatusCode status)
    {
        // Arrange
        _handler.Respond(status, Error("rate_limit_exceeded", (int)status));

        // Act
        var thrown = await Assert.ThrowsAsync<TransientEmailFailureException>(() => Sender().SendAsync(Email(), default));

        // Assert
        await Assert.That(thrown!.Message).DoesNotContain(ApiKey).And.DoesNotContain("secret text");
    }

    [Test]
    public async Task A_409_for_a_concurrent_idempotent_request_is_transient()
    {
        // Arrange
        _handler.Respond(HttpStatusCode.Conflict, Error("concurrent_idempotent_requests", 409));

        // Act and assert
        await Assert.ThrowsAsync<TransientEmailFailureException>(() => Sender().SendAsync(Email(), default));
    }

    [Test]
    public async Task A_409_for_an_invalid_idempotent_request_alerts_and_is_not_recorded_as_failed()
    {
        // Arrange
        using var capture = new MetricCapture(_services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>());
        _handler.Respond(HttpStatusCode.Conflict, Error("invalid_idempotent_request", 409));

        // Act
        var thrown = await Assert.ThrowsAsync<EmailIdempotencyConflictException>(() => Sender().SendAsync(Email(), default));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(thrown!.Message).DoesNotContain("secret text");
            await Assert.That(_logs.Entries.Any(e => e.Level == LogLevel.Error && e.Message.Contains("invalid_idempotent_request"))).IsTrue();
            await Assert.That(capture.Of(EmailProviderMetrics.RequestsMetric).Single().Tags[EmailProviderMetrics.OutcomeTag])
                .IsEqualTo("idempotency_conflict");
        }
    }

    [Test]
    [Arguments(HttpStatusCode.BadRequest, "validation_error")]
    [Arguments(HttpStatusCode.UnprocessableEntity, "invalid_parameter")]
    public async Task Any_other_4xx_fails_for_good_with_a_code_and_logs_only_the_status_and_the_error_name(HttpStatusCode status, string name)
    {
        // Arrange
        using var capture = new MetricCapture(_services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>());
        _handler.Respond(status, Error(name, (int)status));

        // Act
        var outcome = await Sender().SendAsync(Email(), default);

        // Assert
        var logged = string.Join('\n', _logs.Entries.Select(e => e.Message));
        using (Assert.Multiple())
        {
            await Assert.That(outcome).IsEqualTo(new ChannelOutcome.Failed(DeliveryFailureReason.ProviderRejected));
            await Assert.That(logged).Contains(name).And.Contains(((int)status).ToString(System.Globalization.CultureInfo.InvariantCulture));
            await Assert.That(logged).DoesNotContain("secret text").And.DoesNotContain(ApiKey).And.DoesNotContain("ana@example.com");
            await Assert.That(capture.Of(EmailProviderMetrics.RequestsMetric).Single().Tags[EmailProviderMetrics.OutcomeTag]).IsEqualTo("rejected");
        }
    }

    [Test]
    [Arguments(HttpStatusCode.Unauthorized, "missing_api_key")]
    [Arguments(HttpStatusCode.Forbidden, "invalid_api_key")]
    public async Task A_401_or_a_403_is_a_configuration_fault_that_is_counted_logged_as_an_error_and_thrown_never_failed(
        HttpStatusCode status, string name)
    {
        // Arrange
        using var capture = new MetricCapture(_services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>());
        _handler.Respond(status, Error(name, (int)status));

        // Act
        var thrown = await Assert.ThrowsAsync<EmailProviderConfigurationException>(() => Sender().SendAsync(Email(), default));

        // Assert
        var logged = string.Join('\n', _logs.Entries.Where(e => e.Level == LogLevel.Error).Select(e => e.Message));
        using (Assert.Multiple())
        {
            await Assert.That(thrown!.Message).DoesNotContain("secret text").And.DoesNotContain(ApiKey);
            await Assert.That(logged).Contains(name).And.DoesNotContain(ApiKey).And.DoesNotContain("ana@example.com");
            await Assert.That(capture.Of(EmailProviderMetrics.RequestsMetric).Single().Tags[EmailProviderMetrics.OutcomeTag])
                .IsEqualTo("unauthorized");
        }
    }

    [Test]
    public async Task A_network_fault_or_a_timeout_is_counted_transient_and_thrown_as_transient_never_failed()
    {
        // Arrange
        using var capture = new MetricCapture(_services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>());
        _handler.Throw(new HttpRequestException("connection reset")).Throw(new TaskCanceledException("timed out", new TimeoutException()));

        // Act
        var network = await Assert.ThrowsAsync<TransientEmailFailureException>(() => Sender().SendAsync(Email(), default));
        var timeout = await Assert.ThrowsAsync<TransientEmailFailureException>(() => Sender().SendAsync(Email(), default));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(network!.InnerException).IsTypeOf<HttpRequestException>();
            await Assert.That(timeout!.InnerException).IsTypeOf<TaskCanceledException>();
            await Assert.That(capture.Of(EmailProviderMetrics.RequestsMetric).Select(m => (string)m.Tags[EmailProviderMetrics.OutcomeTag]!))
                .IsEquivalentTo(["transient", "transient"]);
        }
    }

    [Test]
    public async Task An_open_circuit_a_polly_timeout_and_a_refused_lease_are_counted_transient_and_thrown_as_transient()
    {
        // Arrange
        using var capture = new MetricCapture(_services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>());
        _handler.Throw(new BrokenCircuitException()).Throw(new TimeoutRejectedException()).Throw(RateLimiterRejection());

        // Act
        var thrown = new List<TransientEmailFailureException?>();
        for (var i = 0; i < 3; i++)
        {
            thrown.Add(await Assert.ThrowsAsync<TransientEmailFailureException>(() => Sender().SendAsync(Email(), default)));
        }

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(thrown.Select(t => t!.InnerException!.GetType())).IsEquivalentTo(
                [typeof(BrokenCircuitException), typeof(TimeoutRejectedException), typeof(RateLimiterRejectedException)]);
            await Assert.That(capture.Of(EmailProviderMetrics.RequestsMetric).Select(m => (string)m.Tags[EmailProviderMetrics.OutcomeTag]!))
                .IsEquivalentTo(["transient", "transient", "transient"]);
        }
    }

    [Test]
    public async Task A_cancellation_the_caller_asked_for_is_not_counted_and_passes_through()
    {
        // Arrange
        using var capture = new MetricCapture(_services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        _handler.Hang();

        // Act and assert
        await Assert.ThrowsAsync<TaskCanceledException>(() => Sender().SendAsync(Email(), cts.Token));
        await Assert.That(capture.Of(EmailProviderMetrics.RequestsMetric)).IsEmpty();
    }

    private static RateLimiterRejectedException RateLimiterRejection() => new(TimeSpan.FromSeconds(1));

    [Test]
    public async Task A_2xx_without_a_message_id_is_transient_because_nothing_can_be_matched_to_it()
    {
        // Arrange
        _handler.Respond(HttpStatusCode.OK, "{}");

        // Act and assert
        await Assert.ThrowsAsync<TransientEmailFailureException>(() => Sender().SendAsync(Email(), default));
    }

    [Test]
    public async Task A_sent_email_is_counted_as_sent()
    {
        // Arrange
        using var capture = new MetricCapture(_services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>());
        _handler.Respond(HttpStatusCode.OK, """{"id":"re_msg_1"}""");

        // Act
        await Sender().SendAsync(Email(), default);

        // Assert
        var counted = capture.Of(EmailProviderMetrics.RequestsMetric).Single();
        using (Assert.Multiple())
        {
            await Assert.That(counted.Tags[EmailProviderMetrics.ProviderTag]).IsEqualTo("resend");
            await Assert.That(counted.Tags[EmailProviderMetrics.OutcomeTag]).IsEqualTo("sent");
        }
    }
}
