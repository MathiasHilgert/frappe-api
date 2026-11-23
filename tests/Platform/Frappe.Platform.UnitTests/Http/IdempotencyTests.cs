using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Frappe.Http;
using Frappe.Platform.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Frappe.Platform.UnitTests.Http;

/// <summary>HTTP-26 / CQRS-10: an endpoint that opts in replays its first answer to the same Idempotency-Key for 24 hours.</summary>
public sealed class IdempotencyTests
{
    /// <summary>The request body of the sample creation endpoint.</summary>
    private sealed record Order(string Item, int Count);

    /// <summary>The answer of the sample creation endpoint.</summary>
    private sealed record Created(int Number, string Item);

    /// <summary>Counts how often the handler ran and lets a test hold it open.</summary>
    private sealed class Counter
    {
        public int Runs;
        public TaskCompletionSource? Gate;
        public TaskCompletionSource Started { get; } = new();
    }

    /// <summary>An idempotency store whose claim and completion always fail.</summary>
    private sealed class BrokenStore : IIdempotencyStore
    {
        public ValueTask<IdempotencyClaim> ClaimAsync(string key, string fingerprint, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("the store is down");

        public ValueTask CompleteAsync(string key, string fingerprint, StoredResponse response, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("the store is down");

        public ValueTask ReleaseAsync(string key, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    /// <summary>A response stream that fails when written to.</summary>
    private sealed class FailingWriteStream(Stream inner) : Stream
    {
        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new IOException("the client left");

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            throw new IOException("the client left");
    }

    private static async Task<WebApplication> StartAsync(Counter counter, FakeTimeProvider? clock = null, IIdempotencyStore? store = null)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<TimeProvider>(clock ?? new FakeTimeProvider(new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero)));
        if (store is not null)
        {
            builder.Services.AddSingleton(store);
        }

        builder.Services.AddFrappeHttp(new AnonymousRateLimit(1000, TimeSpan.FromMinutes(10))).AddErrorMessages(PlatformTestMessages.Assembly);

        var app = builder.Build();
        app.UseFrappeHttp();
        app.Use((context, next) =>
        {
            if (context.Request.Headers.TryGetValue("X-Test-User", out var user))
            {
                context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.ToString())], "test"));
            }

            if (context.Request.Headers.ContainsKey("X-Test-Fail-Write"))
            {
                context.Response.Body = new FailingWriteStream(context.Response.Body);
            }

            return next();
        });
        var v1 = app.MapGroup("/v1").NegotiateRequestLanguage();
        v1.MapPost("/orders", async Task<Results<Created<Created>, Frappe.Http.ErrorsProblemHttpResult>> (Order order, HttpContext context) =>
        {
            var number = Interlocked.Increment(ref counter.Runs);
            counter.Started.TrySetResult();
            if (counter.Gate is { } gate)
            {
                await gate.Task;
            }

            context.Response.Cookies.Append("session", "secret", new CookieOptions { HttpOnly = true, Secure = true });
            if (order.Count < 1)
            {
                return Frappe.Http.ErrorsProblemHttpResult.Of(Frappe.Results.Error.InvalidValue("test.order.invalid"));
            }

            return TypedResults.Created($"/v1/orders/{number}", new Created(number, order.Item));
        }).AcceptsIdempotencyKey().NoStore();
        v1.MapPost("/boom", string () =>
        {
            Interlocked.Increment(ref counter.Runs);
            throw new InvalidOperationException("boom");
        }).AcceptsIdempotencyKey();
        v1.MapPost("/plain", () => TypedResults.Created("/v1/plain/1", new Created(Interlocked.Increment(ref counter.Runs), "plain")));
        v1.MapPost("/other", () => TypedResults.Created("/v1/other/1",
            new Created(Interlocked.Increment(ref counter.Runs), "other"))).AcceptsIdempotencyKey();
        await app.StartAsync();
        return app;
    }

    private static async Task<HttpResponseMessage> PostAsync(WebApplication app, string path, object? body, string? key, string? user = null,
        string? language = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body ?? new { }) };
        if (key is not null)
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        }

        if (user is not null)
        {
            request.Headers.Add("X-Test-User", user);
        }

        if (language is not null)
        {
            request.Headers.TryAddWithoutValidation("Accept-Language", language);
        }

        return await app.GetTestClient().SendAsync(request);
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        JsonElement.Parse(await response.Content.ReadAsStringAsync()).GetProperty("errors")[0].GetProperty("code").GetString();

    [Test]
    public async Task A_creation_whose_answer_could_not_be_written_to_the_client_is_replayed_not_run_again()
    {
        // Arrange
        var counter = new Counter();
        await using var app = await StartAsync(counter);

        using var failing = new HttpRequestMessage(HttpMethod.Post, "/v1/orders") { Content = JsonContent.Create(new Order("tea", 2)) };
        failing.Headers.TryAddWithoutValidation("Idempotency-Key", "abort-1");
        failing.Headers.Add("X-Test-Fail-Write", "1");
        try
        {
            using var _ = await app.GetTestClient().SendAsync(failing);
        }
        catch (Exception exception) when (exception is IOException or HttpRequestException)
        {
            // The client never got the answer: that is the point.
        }

        // Act
        using var retry = await PostAsync(app, "/v1/orders", new Order("tea", 2), "abort-1");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(counter.Runs).IsEqualTo(1);
            await Assert.That(retry.StatusCode).IsEqualTo(HttpStatusCode.Created);
            await Assert.That(retry.Headers.GetValues("Idempotent-Replayed").Single()).IsEqualTo("true");
            await Assert.That(retry.Headers.Location?.OriginalString).IsEqualTo("/v1/orders/1");
        }
    }

    [Test]
    public async Task The_same_key_replays_the_first_answer_without_running_the_endpoint_again()
    {
        // Arrange
        var counter = new Counter();
        await using var app = await StartAsync(counter);

        using var first = await PostAsync(app, "/v1/orders", new Order("tea", 2), "7f3c-1");

        // Act
        using var second = await PostAsync(app, "/v1/orders", new Order("tea", 2), "7f3c-1");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(counter.Runs).IsEqualTo(1);
            await Assert.That(first.StatusCode).IsEqualTo(HttpStatusCode.Created);
            await Assert.That(first.Headers.Contains("Idempotent-Replayed")).IsFalse();
            await Assert.That(second.StatusCode).IsEqualTo(HttpStatusCode.Created);
            await Assert.That(second.Headers.Location?.OriginalString).IsEqualTo("/v1/orders/1");
            await Assert.That(second.Headers.Location).IsEqualTo(first.Headers.Location);
            await Assert.That(second.Headers.GetValues("Idempotent-Replayed")).IsEquivalentTo(["true"]);
            await Assert.That(second.Headers.CacheControl?.NoStore).IsTrue();
            await Assert.That(second.Content.Headers.ContentType?.MediaType).IsEqualTo("application/json");
            await Assert.That(await second.Content.ReadAsStringAsync()).IsEqualTo(await first.Content.ReadAsStringAsync());
        }
    }

    [Test]
    public async Task A_request_without_the_header_or_with_another_key_runs_every_time()
    {
        // Arrange
        var counter = new Counter();
        await using var app = await StartAsync(counter);

        using var none = await PostAsync(app, "/v1/orders", new Order("tea", 1), key: null);
        using var noneAgain = await PostAsync(app, "/v1/orders", new Order("tea", 1), key: null);
        using var one = await PostAsync(app, "/v1/orders", new Order("tea", 1), "key-a");

        // Act
        using var other = await PostAsync(app, "/v1/orders", new Order("tea", 1), "key-b");

        // Assert
        await Assert.That(counter.Runs).IsEqualTo(4);
    }

    [Test]
    public async Task An_endpoint_that_did_not_opt_in_ignores_the_header()
    {
        // Arrange
        var counter = new Counter();
        await using var app = await StartAsync(counter);

        using var first = await PostAsync(app, "/v1/plain", null, "same");

        // Act
        using var second = await PostAsync(app, "/v1/plain", null, "same");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(counter.Runs).IsEqualTo(2);
            await Assert.That(second.Headers.Contains("Idempotent-Replayed")).IsFalse();
        }
    }

    [Test]
    public async Task The_key_belongs_to_one_endpoint_and_one_caller()
    {
        // Arrange
        var counter = new Counter();
        await using var app = await StartAsync(counter);

        using var orders = await PostAsync(app, "/v1/orders", new Order("tea", 1), "shared", user: "alice");
        using var other = await PostAsync(app, "/v1/other", null, "shared", user: "alice");
        using var bob = await PostAsync(app, "/v1/orders", new Order("tea", 1), "shared", user: "bob");

        // Act
        using var aliceAgain = await PostAsync(app, "/v1/orders", new Order("tea", 1), "shared", user: "alice");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(counter.Runs).IsEqualTo(3);
            await Assert.That(aliceAgain.Headers.GetValues("Idempotent-Replayed")).IsEquivalentTo(["true"]);
            await Assert.That(await aliceAgain.Content.ReadAsStringAsync()).IsEqualTo(await orders.Content.ReadAsStringAsync());
        }
    }

    [Test]
    public async Task The_same_key_with_another_request_is_refused_as_a_reused_key()
    {
        // Arrange
        var counter = new Counter();
        await using var app = await StartAsync(counter);
        using var first = await PostAsync(app, "/v1/orders", new Order("tea", 1), "key");

        // Act
        using var reused = await PostAsync(app, "/v1/orders", new Order("coffee", 1), "key");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(counter.Runs).IsEqualTo(1);
            await Assert.That(reused.StatusCode).IsEqualTo(HttpStatusCode.UnprocessableEntity);
            await Assert.That(await CodeAsync(reused)).IsEqualTo("platform.idempotency_key.reused");
        }
    }

    [Test]
    public async Task An_answer_is_replayed_for_24_hours_and_the_endpoint_runs_again_after_that()
    {
        // Arrange
        var counter = new Counter();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero));
        await using var app = await StartAsync(counter, clock);
        using var first = await PostAsync(app, "/v1/orders", new Order("tea", 1), "key");

        clock.Advance(TimeSpan.FromHours(23) + TimeSpan.FromMinutes(59));
        using var inside = await PostAsync(app, "/v1/orders", new Order("tea", 1), "key");
        clock.Advance(TimeSpan.FromMinutes(2));

        // Act
        using var outside = await PostAsync(app, "/v1/orders", new Order("tea", 1), "key");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(IdempotencyKey.Retention).IsEqualTo(TimeSpan.FromHours(24));
            await Assert.That(inside.Headers.Contains("Idempotent-Replayed")).IsTrue();
            await Assert.That(outside.Headers.Contains("Idempotent-Replayed")).IsFalse();
            await Assert.That(counter.Runs).IsEqualTo(2);
        }
    }

    [Test]
    public async Task A_refused_request_is_not_stored_so_the_same_key_can_be_retried()
    {
        // Arrange
        var counter = new Counter();
        await using var app = await StartAsync(counter);

        using var refused = await PostAsync(app, "/v1/orders", new Order("tea", 0), "key");

        // Act
        using var retried = await PostAsync(app, "/v1/orders", new Order("tea", 0), "key");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(refused.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
            await Assert.That(await CodeAsync(refused)).IsEqualTo("test.order.invalid");
            await Assert.That(retried.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
            await Assert.That(retried.Headers.Contains("Idempotent-Replayed")).IsFalse();
            await Assert.That(counter.Runs).IsEqualTo(2);
        }
    }

    [Test]
    public async Task A_failed_run_releases_the_key_so_the_client_can_retry()
    {
        // Arrange
        var counter = new Counter();
        await using var app = await StartAsync(counter);

        using var failed = await PostAsync(app, "/v1/boom", null, "key");

        // Act
        using var retried = await PostAsync(app, "/v1/boom", null, "key");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(failed.StatusCode).IsEqualTo(HttpStatusCode.InternalServerError);
            await Assert.That(retried.StatusCode).IsEqualTo(HttpStatusCode.InternalServerError);
            await Assert.That(counter.Runs).IsEqualTo(2);
        }
    }

    [Test]
    public async Task A_request_that_arrives_while_the_first_still_runs_is_a_conflict_and_never_runs_twice()
    {
        // Arrange
        var counter = new Counter { Gate = new TaskCompletionSource() };
        await using var app = await StartAsync(counter);

        var first = PostAsync(app, "/v1/orders", new Order("tea", 1), "key");
        await counter.Started.Task;
        using var second = await PostAsync(app, "/v1/orders", new Order("tea", 1), "key");
        counter.Gate.SetResult();

        // Act
        using var finished = await first;

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(second.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
            await Assert.That(await CodeAsync(second)).IsEqualTo("platform.idempotency_key.in_progress");
            await Assert.That(finished.StatusCode).IsEqualTo(HttpStatusCode.Created);
            await Assert.That(counter.Runs).IsEqualTo(1);
        }
    }

    [Test]
    public async Task A_cookie_is_never_replayed()
    {
        // Arrange
        var counter = new Counter();
        await using var app = await StartAsync(counter);

        using var first = await PostAsync(app, "/v1/orders", new Order("tea", 1), "key");

        // Act
        using var second = await PostAsync(app, "/v1/orders", new Order("tea", 1), "key");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(first.Headers.Contains("Set-Cookie")).IsTrue();
            await Assert.That(second.Headers.Contains("Set-Cookie")).IsFalse();
        }
    }

    [Test]
    [Arguments("   ")]
    [Arguments("has space")]
    [Arguments("tab\there")]
    [Arguments("ñandú")]
    public async Task A_key_that_is_empty_or_not_printable_ascii_is_an_invalid_value(string key)
    {
        // Arrange
        var counter = new Counter();
        await using var app = await StartAsync(counter);

        // Act
        using var response = await PostAsync(app, "/v1/orders", new Order("tea", 1), key);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest).Because($"key '{key}'");
            await Assert.That(await CodeAsync(response)).IsEqualTo("platform.idempotency_key.invalid");
            await Assert.That(counter.Runs).IsEqualTo(0);
        }
    }

    [Test]
    public async Task A_key_longer_than_the_limit_is_an_invalid_value_and_one_of_the_limit_is_taken()
    {
        // Arrange
        var counter = new Counter();
        await using var app = await StartAsync(counter);

        using var tooLong = await PostAsync(app, "/v1/orders", new Order("tea", 1), new string('k', IdempotencyKey.MaxLength + 1));

        // Act
        using var longest = await PostAsync(app, "/v1/orders", new Order("tea", 1), new string('k', IdempotencyKey.MaxLength));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(tooLong.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
            await Assert.That(longest.StatusCode).IsEqualTo(HttpStatusCode.Created);
        }
    }

    [Test]
    public async Task Two_Idempotency_Key_headers_are_an_invalid_value()
    {
        // Arrange
        var counter = new Counter();
        await using var app = await StartAsync(counter);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/orders") { Content = JsonContent.Create(new Order("tea", 1)) };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", ["a", "b"]);

        // Act
        using var response = await app.GetTestClient().SendAsync(request);

        // Assert
        await Assert.That(await CodeAsync(response)).IsEqualTo("platform.idempotency_key.invalid");
    }

    [Test]
    public async Task When_the_store_is_down_a_request_with_a_key_is_a_503_that_never_runs_and_one_without_it_still_runs()
    {
        // Arrange
        var counter = new Counter();
        await using var app = await StartAsync(counter, store: new BrokenStore());

        using var keyed = await PostAsync(app, "/v1/orders", new Order("tea", 1), "key");

        // Act
        using var plain = await PostAsync(app, "/v1/orders", new Order("tea", 1), key: null);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(keyed.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
            await Assert.That(await CodeAsync(keyed)).IsEqualTo("platform.idempotency.unavailable");
            await Assert.That(keyed.Headers.RetryAfter?.Delta).IsEqualTo(TimeSpan.FromSeconds(30));
            await Assert.That(plain.StatusCode).IsEqualTo(HttpStatusCode.Created);
            await Assert.That(counter.Runs).IsEqualTo(1);
        }
    }

    [Test]
    public async Task The_replay_keeps_the_language_of_the_first_answer()
    {
        // Arrange
        var counter = new Counter();
        await using var app = await StartAsync(counter);

        using var first = await PostAsync(app, "/v1/orders", new Order("tea", 1), "key", language: "es");

        // Act
        using var second = await PostAsync(app, "/v1/orders", new Order("tea", 1), "key", language: "pt-BR");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(second.Headers.Vary.ToArray()).Contains("Accept-Language");
            await Assert.That(await second.Content.ReadAsStringAsync()).IsEqualTo(await first.Content.ReadAsStringAsync());
        }
    }
}
