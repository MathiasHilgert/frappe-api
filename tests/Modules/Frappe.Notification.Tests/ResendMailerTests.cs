using System.Net;
using System.Text.Json;
using Frappe.Notification.Domain;
using Frappe.Notification.Infrastructure;
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

        await mailer.SendAsync(Mail, TestContext.Current.CancellationToken);

        handler.Request!.Method.ShouldBe(HttpMethod.Post);
        handler.Request.RequestUri.ShouldBe(new Uri("https://api.resend.test/emails"));
        handler.Request.Headers.Authorization!.ToString().ShouldBe("Bearer re_test_key");
        using var body = JsonDocument.Parse(handler.Body!);
        body.RootElement.GetProperty("from").GetString().ShouldBe("Frappé <no-reply@example.test>");
        body.RootElement.GetProperty("to")[0].GetString().ShouldBe("owner@example.test");
        body.RootElement.GetProperty("subject").GetString().ShouldBe("123456 is your code");
        body.RootElement.GetProperty("text").GetString().ShouldBe("Your code is 123456.");
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task SendAsync_ThrowsWhenResendRejects_SoTheQueueRetries(HttpStatusCode status)
    {
        var mailer = Create(new RecordingHandler(status));

        var error = await Should.ThrowAsync<HttpRequestException>(() => mailer.SendAsync(Mail, TestContext.Current.CancellationToken));

        error.StatusCode.ShouldBe(status);
    }

    private static ResendMailer Create(RecordingHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://api.resend.test/") },
            Options.Create(new ResendOptions { ApiKey = "re_test_key", From = "Frappé <no-reply@example.test>" }));

    private sealed class RecordingHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status);
        }
    }
}
