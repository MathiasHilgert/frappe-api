using System.Net;
using System.Text.Json;
using Frappe.Notification.Application.Mails;
using Frappe.Notification.Domain;
using Frappe.Notification.Infrastructure.Resend;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Frappe.Notification.Tests;

public sealed class ResendMailerTests
{
    private static readonly Mail Mail = new("owner@example.test", "123456 is your code", "Your code is 123456.");

    [Fact]
    public async Task SendAsync_PostsTheMailToResendWithTheApiKey()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK);
        var mailer = Create(handler);

        await mailer.SendAsync(Mail, "mail-key", TestContext.Current.CancellationToken);

        handler.Request!.Method.ShouldBe(HttpMethod.Post);
        handler.Request.RequestUri.ShouldBe(new Uri("https://api.resend.test/emails"));
        handler.Request.Headers.Authorization!.ToString().ShouldBe("Bearer re_test_key");
        using var body = JsonDocument.Parse(handler.Body!);
        body.RootElement.GetProperty("from").GetString().ShouldBe("Frappé <no-reply@example.test>");
        body.RootElement.GetProperty("to")[0].GetString().ShouldBe("owner@example.test");
        body.RootElement.GetProperty("subject").GetString().ShouldBe("123456 is your code");
        body.RootElement.GetProperty("text").GetString().ShouldBe("Your code is 123456.");
        body.RootElement.TryGetProperty("html", out _).ShouldBeFalse();
        body.RootElement.TryGetProperty("attachments", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task SendAsync_SendsTheHtmlPart_WithInlineImagesAsContentIdAttachments()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK);
        var mail = new Mail("owner@example.test", "123456 is your code", "Your code is 123456.", "<img src=\"cid:logo\">",
            [new InlineImage("logo", "logo.png", "image/png", new byte[] { 1, 2, 3 })]);

        await Create(handler).SendAsync(mail, "mail-key", TestContext.Current.CancellationToken);

        using var body = JsonDocument.Parse(handler.Body!);
        body.RootElement.GetProperty("html").GetString().ShouldBe("<img src=\"cid:logo\">");
        var attachment = body.RootElement.GetProperty("attachments").EnumerateArray().ShouldHaveSingleItem();
        attachment.GetProperty("filename").GetString().ShouldBe("logo.png");
        attachment.GetProperty("content").GetString().ShouldBe("AQID");
        attachment.GetProperty("content_type").GetString().ShouldBe("image/png");
        attachment.GetProperty("content_id").GetString().ShouldBe("logo");
    }

    [Fact]
    public async Task SendAsync_SendsTheDeliverysIdempotencyKey_SoARetryAfterATimeoutDoesNotSendTwice()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK);
        var key = MailDelivery.KeyFor(Guid.CreateVersion7());

        await Create(handler).SendAsync(Mail, key, TestContext.Current.CancellationToken);

        handler.Request!.Headers.GetValues("Idempotency-Key").ShouldHaveSingleItem().ShouldBe(key);
    }

    [Fact]
    public void DeliveryKeys_DependOnTheMessage_NotOnTheContent()
    {
        var id = Guid.CreateVersion7();

        MailDelivery.KeyFor(id).ShouldBe(MailDelivery.KeyFor(id));
        MailDelivery.KeyFor(id).ShouldNotBe(MailDelivery.KeyFor(Guid.CreateVersion7()), StringComparer.Ordinal);
        MailDelivery.KeyFor(id).Length.ShouldBeInRange(1, 256);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task SendAsync_ThrowsMailRejected_OnAClientError_SoItIsDeadLetteredWithoutRetries(HttpStatusCode status)
    {
        var mailer = Create(new RecordingHandler(status));

        var error = await Should.ThrowAsync<MailRejectedException>(() => mailer.SendAsync(Mail, "mail-key", TestContext.Current.CancellationToken));

        error.StatusCode.ShouldBe(status);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task SendAsync_ThrowsMailDeliveryFailed_OnThrottlingOrAServerError_SoTheQueueRetries(HttpStatusCode status)
    {
        var mailer = Create(new RecordingHandler(status));

        var error = await Should.ThrowAsync<MailDeliveryFailedException>(() => mailer.SendAsync(Mail, "mail-key", TestContext.Current.CancellationToken));

        error.StatusCode.ShouldBe(status);
    }

    [Fact]
    public async Task SendAsync_ThrowsMailDeliveryFailed_OnATimeout()
    {
        var mailer = Create(new RecordingHandler(HttpStatusCode.OK, new TaskCanceledException("timeout", new TimeoutException())));

        await Should.ThrowAsync<MailDeliveryFailedException>(() => mailer.SendAsync(Mail, "mail-key", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SendAsync_ThrowsMailDeliveryFailed_OnANetworkFailure()
    {
        var mailer = Create(new RecordingHandler(HttpStatusCode.OK, new HttpRequestException("connection refused")));

        await Should.ThrowAsync<MailDeliveryFailedException>(() => mailer.SendAsync(Mail, "mail-key", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SendAsync_LetsCancellationThrough_WhenTheCallerCancels()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var mailer = Create(new RecordingHandler(HttpStatusCode.OK, new TaskCanceledException()));

        await Should.ThrowAsync<OperationCanceledException>(() => mailer.SendAsync(Mail, "mail-key", cancellation.Token));
    }

    private static ResendMailer Create(RecordingHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://api.resend.test/") },
            Options.Create(new ResendOptions { ApiKey = "re_test_key", From = "Frappé <no-reply@example.test>" }));

    private sealed class RecordingHandler(HttpStatusCode status, Exception? failure = null) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            if (failure is not null)
                throw failure;
            return new HttpResponseMessage(status);
        }
    }
}
