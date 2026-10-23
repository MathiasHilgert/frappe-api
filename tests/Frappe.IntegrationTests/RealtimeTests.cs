using System.Net;
using System.Text.Json;
using Frappe.Application.Abstractions.Realtime;
using Frappe.Contracts.Abstractions.Realtime;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Wolverine;

namespace Frappe.IntegrationTests;

public sealed record ProbePublished(Guid EventId, Guid BusinessId, string Value) : IRealtimeNotification
{
    public string Topic => "test.probe.published";
}

public sealed class RealtimeTests(ApiFixture api)
{
    [Fact]
    public async Task Stream_Anonymous_Returns401()
    {
        await api.Host.Scenario(s =>
        {
            s.Get.Url($"/v1/stream?businessId={Guid.CreateVersion7()}");
            s.StatusCodeShouldBe(HttpStatusCode.Unauthorized);
        });
    }

    [Fact]
    public async Task Stream_PushPublishedThroughTheBus_ReachesTheBusinessStreamWithResumableId()
    {
        await using var host = await api.CreateHostAsync(services => services.AuthenticateEveryone());
        var businessId = Guid.CreateVersion7();
        var notification = new ProbePublished(Guid.CreateVersion7(), businessId, "hello");

        await using (var scope = host.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IMessageBus>().PublishAsync(RealtimePush.From(notification));

        var lines = await ReadFirstEventAsync(host.Server.CreateClient(), businessId);

        lines.ShouldContain(l => string.Equals(l, "event: test.probe.published", StringComparison.Ordinal));
        lines.ShouldContain(l => l.StartsWith("data: ", StringComparison.Ordinal) && l.Contains("\"value\":\"hello\"", StringComparison.Ordinal));
        var id = lines.Single(l => l.StartsWith("id: ", StringComparison.Ordinal))["id: ".Length..];
        ulong.Parse(id, System.Globalization.CultureInfo.InvariantCulture).ShouldBeGreaterThan(0UL);
    }

    [Fact]
    public async Task Stream_AuthenticatedWithoutBusinessId_Returns400ProblemWithRequestId()
    {
        await using var host = await api.CreateHostAsync(services => services.AuthenticateEveryone());
        using var client = host.Server.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/stream");
        request.Headers.Add("X-Request-Id", "corr-400");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        var body = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        body.GetProperty("status").GetInt32().ShouldBe(400);
        body.GetProperty("requestId").GetString().ShouldBe("corr-400");
    }

    [Fact]
    public async Task Stream_AnonymousWithoutBusinessId_StillReturns401()
    {
        await api.Host.Scenario(s =>
        {
            s.Get.Url("/v1/stream");
            s.StatusCodeShouldBe(HttpStatusCode.Unauthorized);
        });
    }

    /// <summary>Reads the lines of the first server-sent event, up to its terminating blank line.</summary>
    private static async Task<List<string>> ReadFirstEventAsync(HttpClient client, Guid businessId)
    {
        // Last-Event-ID 0 replays this business's stream from the start, so the read cannot miss the event
        // no matter whether it was pushed before or after the request connected.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using (client)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/v1/stream?businessId={businessId}");
            request.Headers.Add("Last-Event-ID", "0");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(timeout.Token));

            var lines = new List<string>();
            while (lines.Count == 0 || lines[^1].Length > 0)
                lines.Add(await reader.ReadLineAsync(timeout.Token) ?? throw new EndOfStreamException());

            return lines;
        }
    }
}
