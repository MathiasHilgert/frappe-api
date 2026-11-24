using System.Net;
using Frappe.Localization;
using Frappe.Notifications.Application.Channels;
using Frappe.Notifications.Channels.Email;
using Frappe.Notifications.Domain.Deliveries;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Polly.Timeout;

namespace Frappe.Notifications.UnitTests.Emails;

/// <summary>
/// The Resend client as the host composes it, over a fake transport: authorized, retried on 429 and 5xx with the same idempotency key,
/// never retried on a refusal, bounded by its budgets and held under the provider's rate limit (CFG-08, S14).
/// </summary>
public sealed class ResendClientPipelineTests
{
    private static RenderedEmail Email(string key = "idem-key-1") => new(
        EmailAddress.Create("ana@example.com").Value, "Subject", "<p>x</p>", "x", new Dictionary<string, string>(), key,
        LanguageTag.Create("en").Value);

    private static ServiceProvider Compose(StubHandler handler, Dictionary<string, string?>? extra = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Notifications:Email:Resend:ApiKey"] = "re_pipeline_key",
            ["Notifications:Email:Resend:RetryDelay"] = "00:00:00",
            ["Notifications:Email:Resend:Retries"] = "2",
        };
        foreach (var (key, value) in extra ?? [])
        {
            settings[key] = value;
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        services.AddEmailSender();
        services.AddHttpClient<ResendEmailSender>().ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider();
    }

    [Test]
    public async Task Every_attempt_is_authorized_to_the_emails_endpoint_and_a_retry_repeats_the_same_idempotency_key()
    {
        // Arrange
        using var handler = new StubHandler()
            .Respond(HttpStatusCode.ServiceUnavailable).Respond(HttpStatusCode.TooManyRequests).Respond(HttpStatusCode.OK, """{"id":"re_1"}""");
        await using var provider = Compose(handler);

        // Act
        var outcome = await provider.GetRequiredService<ResendEmailSender>().SendAsync(Email(), default);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(outcome).IsEqualTo(new ChannelOutcome.Sent(DeliveryProvider.Resend, "re_1", null));
            await Assert.That(handler.Received.Count).IsEqualTo(3);
            await Assert.That(handler.Received.Select(r => r.Request.RequestUri!.AbsoluteUri).Distinct())
                .IsEquivalentTo(["https://api.resend.com/emails"]);
            await Assert.That(handler.Received.Select(r => r.Request.Headers.Authorization!.ToString()).Distinct())
                .IsEquivalentTo(["Bearer re_pipeline_key"]);
            await Assert.That(handler.Received.Select(r => r.Request.Headers.GetValues("Idempotency-Key").Single()).Distinct())
                .IsEquivalentTo(["idem-key-1"]);
            await Assert.That(handler.Received.Select(r => r.Body).Distinct().Count()).IsEqualTo(1);
        }
    }

    [Test]
    public async Task A_failure_that_outlasts_its_retries_throws_transient_and_is_never_a_failed_outcome()
    {
        // Arrange
        using var handler = new StubHandler()
            .Respond(HttpStatusCode.BadGateway).Respond(HttpStatusCode.BadGateway).Respond(HttpStatusCode.BadGateway);
        await using var provider = Compose(handler);

        // Act
        await Assert.ThrowsAsync<TransientEmailFailureException>(() => provider.GetRequiredService<ResendEmailSender>().SendAsync(Email(), default));

        // Assert
        await Assert.That(handler.Received.Count).IsEqualTo(3);
    }

    [Test]
    public async Task A_refusal_is_not_retried()
    {
        // Arrange
        using var handler = new StubHandler().Respond(HttpStatusCode.UnprocessableEntity, """{"name":"validation_error"}""");
        await using var provider = Compose(handler);

        // Act
        var outcome = await provider.GetRequiredService<ResendEmailSender>().SendAsync(Email(), default);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(outcome).IsEqualTo(new ChannelOutcome.Failed(DeliveryFailureReason.ProviderRejected));
            await Assert.That(handler.Received.Count).IsEqualTo(1);
        }
    }

    [Test]
    public async Task A_provider_that_never_answers_is_cut_by_the_attempt_budget_and_throws_transient()
    {
        // Arrange
        using var handler = new StubHandler().Hang();
        await using var provider = Compose(handler, new()
        {
            ["Notifications:Email:Resend:AttemptTimeout"] = "00:00:00.100",
            ["Notifications:Email:Resend:TotalTimeout"] = "00:00:01",
            ["Notifications:Email:Resend:Retries"] = "1",
        });

        // Act and assert
        var thrown = await Assert.ThrowsAsync<TransientEmailFailureException>(
            () => provider.GetRequiredService<ResendEmailSender>().SendAsync(Email(), default));
        await Assert.That(thrown!.InnerException).IsTypeOf<TimeoutRejectedException>();
    }

    [Test]
    public async Task Sends_beyond_the_configured_rate_wait_for_a_token_instead_of_failing()
    {
        // Arrange
        using var handler = new StubHandler();
        for (var i = 0; i < 4; i++)
        {
            handler.Respond(HttpStatusCode.OK, $$"""{"id":"re_{{i}}"}""");
        }

        await using var provider = Compose(handler, new() { ["Notifications:Email:Resend:RequestsPerSecond"] = "2" });
        var sender = provider.GetRequiredService<ResendEmailSender>();

        // Act
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 4).Select(i => sender.SendAsync(Email($"key-{i}"), default)));

        // Assert
        var times = handler.Received.Select(r => r.At).Order().ToArray();
        using (Assert.Multiple())
        {
            await Assert.That(outcomes.All(o => o is ChannelOutcome.Sent)).IsTrue();
            await Assert.That(times[1] - times[0]).IsLessThan(TimeSpan.FromMilliseconds(500));
            await Assert.That(times[3] - times[0]).IsGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(800));
        }
    }
}
