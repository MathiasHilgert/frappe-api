using System.Net;
using Frappe.Caching;
using Frappe.Http;
using Frappe.Platform.IntegrationTests.Caching;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Frappe.Platform.IntegrationTests.Http;

/// <summary>The answers of requests sent with an <c>Idempotency-Key</c>, kept in a real Valkey that every replica shares.</summary>
[ClassDataSource<ValkeyServer>(Shared = SharedType.PerTestSession)]
public sealed class ValkeyIdempotencyStoreTests(ValkeyServer valkey)
{
    /// <summary>The request body of the sample creation endpoint.</summary>
    private sealed record Order(string Item);

    /// <summary>The answer of the sample creation endpoint.</summary>
    private sealed record Created(int Number, string Item);

    /// <summary>One replica: its own host, sharing Valkey (under <paramref name="instance"/>) and the run counter.</summary>
    private async Task<WebApplication> StartReplicaAsync(string instance, int[] runs)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddFrappeCaching(valkey.ConnectionString, instance);
        builder.Services.AddFrappeHttp();
        var app = builder.Build();
        app.UseFrappeHttp();
        app.MapPost("/v1/orders", Task<Results<Created<Created>, Frappe.Http.ErrorsProblemHttpResult>> (Order order) =>
            Task.FromResult<Results<Created<Created>, Frappe.Http.ErrorsProblemHttpResult>>(
                TypedResults.Created("/v1/orders/1", new Created(Interlocked.Increment(ref runs[0]), order.Item)))).AcceptsIdempotencyKey();
        await app.StartAsync();
        return app;
    }

    private static async Task<HttpResponseMessage> PostAsync(WebApplication app, string item, string key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/orders") { Content = JsonContent.Create(new Order(item)) };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        return await app.GetTestClient().SendAsync(request);
    }

    private static StoredResponse Answer() => new(
        StatusCodes.Status201Created,
        new Dictionary<string, string[]> { ["Location"] = ["/v1/orders/7"], ["Content-Type"] = ["application/json"] },
        "{\"number\":7}"u8.ToArray());

    private IIdempotencyStore StoreOf(string instance)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFrappeCaching(valkey.ConnectionString, instance);
        services.AddFrappeHttp();
        return services.BuildServiceProvider().GetRequiredService<IIdempotencyStore>();
    }

    [Test]
    public async Task With_the_shared_cache_registered_the_answers_are_kept_in_valkey()
    {
        // Arrange
        var instance = $"idem-{Guid.NewGuid():N}";

        // Act
        var store = StoreOf(instance);

        // Assert
        await Assert.That(store.GetType().Name).IsEqualTo("ValkeyIdempotencyStore");
    }

    [Test]
    public async Task A_free_key_is_claimed_and_a_second_request_with_the_same_content_finds_it_in_flight()
    {
        // Arrange
        var store = StoreOf($"idem-{Guid.NewGuid():N}");

        // Act
        var first = await store.ClaimAsync("key", "content", CancellationToken.None);
        var second = await store.ClaimAsync("key", "content", CancellationToken.None);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(first.Outcome).IsEqualTo(IdempotencyOutcome.Claimed);
            await Assert.That(second.Outcome).IsEqualTo(IdempotencyOutcome.InFlight);
            await Assert.That(second.Response).IsNull();
        }
    }

    [Test]
    public async Task The_same_key_with_other_content_is_reused_while_in_flight_and_after_completion()
    {
        // Arrange
        var store = StoreOf($"idem-{Guid.NewGuid():N}");
        var claim = await store.ClaimAsync("key", "content", CancellationToken.None);
        var whileRunning = await store.ClaimAsync("key", "other", CancellationToken.None);

        // Act
        await store.CompleteAsync("key", claim.Owner!, "content", Answer(), CancellationToken.None);
        var afterwards = await store.ClaimAsync("key", "other", CancellationToken.None);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(whileRunning.Outcome).IsEqualTo(IdempotencyOutcome.Reused);
            await Assert.That(afterwards.Outcome).IsEqualTo(IdempotencyOutcome.Reused);
        }
    }

    [Test]
    public async Task A_completed_key_replays_its_stored_answer_to_the_same_content()
    {
        // Arrange
        var store = StoreOf($"idem-{Guid.NewGuid():N}");
        var claim = await store.ClaimAsync("key", "content", CancellationToken.None);
        await store.CompleteAsync("key", claim.Owner!, "content", Answer(), CancellationToken.None);

        // Act
        var replay = await store.ClaimAsync("key", "content", CancellationToken.None);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(replay.Outcome).IsEqualTo(IdempotencyOutcome.Replay);
            await Assert.That(replay.Response!.Status).IsEqualTo(StatusCodes.Status201Created);
            await Assert.That(replay.Response.Headers["Location"]).IsEquivalentTo(["/v1/orders/7"]);
            await Assert.That(replay.Response.Headers["Content-Type"]).IsEquivalentTo(["application/json"]);
            await Assert.That(replay.Response.Body).IsEquivalentTo("{\"number\":7}"u8.ToArray());
        }
    }

    [Test]
    public async Task A_released_key_can_be_claimed_again()
    {
        // Arrange
        var store = StoreOf($"idem-{Guid.NewGuid():N}");
        var claim = await store.ClaimAsync("key", "content", CancellationToken.None);

        // Act
        await store.ReleaseAsync("key", claim.Owner!, CancellationToken.None);
        var again = await store.ClaimAsync("key", "other", CancellationToken.None);

        // Assert
        await Assert.That(again.Outcome).IsEqualTo(IdempotencyOutcome.Claimed);
    }

    [Test]
    public async Task A_claim_lives_for_the_in_flight_lifetime_and_an_answer_for_the_retention_and_the_key_text_never_reaches_valkey()
    {
        // Arrange
        var instance = $"idem-{Guid.NewGuid():N}";
        var store = StoreOf(instance);
        await using var connection = await ConnectionMultiplexer.ConnectAsync(valkey.ConnectionString);
        var database = connection.GetDatabase();
        var keys = () => connection.GetServer(connection.GetEndPoints()[0]).Keys(pattern: $"{instance}:*").Select(k => k.ToString()).ToArray();

        // Act
        var claim = await store.ClaimAsync("secret-key-text", "content", CancellationToken.None);
        var claimed = keys();
        var claimTtl = await database.KeyTimeToLiveAsync(claimed.Single());
        await store.CompleteAsync("secret-key-text", claim.Owner!, "content", Answer(), CancellationToken.None);
        var answerTtl = await database.KeyTimeToLiveAsync(claimed.Single());

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(claimed.Single()).StartsWith($"{instance}:idempotency:");
            await Assert.That(claimed.Single()).DoesNotContain("secret-key-text");
            await Assert.That(claimTtl!.Value).IsLessThanOrEqualTo(IdempotencyKey.InFlightLifetime);
            await Assert.That(claimTtl.Value).IsGreaterThan(IdempotencyKey.InFlightLifetime - TimeSpan.FromSeconds(30));
            await Assert.That(answerTtl!.Value).IsLessThanOrEqualTo(IdempotencyKey.Retention);
            await Assert.That(answerTtl.Value).IsGreaterThan(IdempotencyKey.Retention - TimeSpan.FromMinutes(5));
        }
    }

    [Test]
    public async Task A_request_whose_claim_expired_never_overwrites_the_key_claimed_again_by_a_retry()
    {
        // Arrange
        var instance = $"idem-{Guid.NewGuid():N}";
        var store = StoreOf(instance);
        var slow = await store.ClaimAsync("key", "content", CancellationToken.None);
        await ExpireAsync(instance);
        var retry = await store.ClaimAsync("key", "content", CancellationToken.None);

        // Act
        var slowCompleted = await store.CompleteAsync("key", slow.Owner!, "content", Answer(), CancellationToken.None);
        var whileRetryRuns = await store.ClaimAsync("key", "content", CancellationToken.None);
        var retryCompleted = await store.CompleteAsync("key", retry.Owner!, "content", new StoredResponse(StatusCodes.Status201Created, new Dictionary<string, string[]>(), [2]), CancellationToken.None);
        var replay = await store.ClaimAsync("key", "content", CancellationToken.None);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(retry.Outcome).IsEqualTo(IdempotencyOutcome.Claimed);
            await Assert.That(retry.Owner).IsNotEqualTo(slow.Owner);
            await Assert.That(slowCompleted).IsFalse();
            await Assert.That(whileRetryRuns.Outcome).IsEqualTo(IdempotencyOutcome.InFlight);
            await Assert.That(retryCompleted).IsTrue();
            await Assert.That(replay.Response!.Body).IsEquivalentTo(new byte[] { 2 });
        }
    }

    [Test]
    public async Task A_request_whose_claim_expired_never_releases_the_claim_or_answer_of_a_retry()
    {
        // Arrange
        var instance = $"idem-{Guid.NewGuid():N}";
        var store = StoreOf(instance);
        var slow = await store.ClaimAsync("running", "content", CancellationToken.None);
        var slowToo = await store.ClaimAsync("answered", "content", CancellationToken.None);
        await ExpireAsync(instance);
        await store.ClaimAsync("running", "content", CancellationToken.None);
        var answered = await store.ClaimAsync("answered", "content", CancellationToken.None);
        await store.CompleteAsync("answered", answered.Owner!, "content", Answer(), CancellationToken.None);

        // Act
        var releasedRunning = await store.ReleaseAsync("running", slow.Owner!, CancellationToken.None);
        var releasedAnswered = await store.ReleaseAsync("answered", slowToo.Owner!, CancellationToken.None);
        var running = await store.ClaimAsync("running", "content", CancellationToken.None);
        var replay = await store.ClaimAsync("answered", "content", CancellationToken.None);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(releasedRunning).IsFalse();
            await Assert.That(releasedAnswered).IsFalse();
            await Assert.That(running.Outcome).IsEqualTo(IdempotencyOutcome.InFlight);
            await Assert.That(replay.Outcome).IsEqualTo(IdempotencyOutcome.Replay);
        }
    }

    [Test]
    public async Task A_claim_that_expired_with_no_retry_is_no_longer_owned()
    {
        // Arrange
        var instance = $"idem-{Guid.NewGuid():N}";
        var store = StoreOf(instance);
        var slow = await store.ClaimAsync("key", "content", CancellationToken.None);
        await ExpireAsync(instance);

        // Act
        var completed = await store.CompleteAsync("key", slow.Owner!, "content", Answer(), CancellationToken.None);
        var released = await store.ReleaseAsync("key", slow.Owner!, CancellationToken.None);
        var next = await store.ClaimAsync("key", "content", CancellationToken.None);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(completed).IsFalse();
            await Assert.That(released).IsFalse();
            await Assert.That(next.Outcome).IsEqualTo(IdempotencyOutcome.Claimed);
        }
    }

    /// <summary>Lets every claim under <paramref name="instance"/> expire now, as Valkey does once their lifetime passed.</summary>
    private async Task ExpireAsync(string instance)
    {
        foreach (var key in await valkey.KeysAsync($"{instance}:idempotency:*"))
        {
            await valkey.Inspector.KeyDeleteAsync(key);
        }
    }

    [Test]
    public async Task A_retry_that_lands_on_another_replica_gets_the_same_answer_and_the_endpoint_runs_once()
    {
        // Arrange
        var instance = $"idem-{Guid.NewGuid():N}";
        var runs = new int[1];
        await using var one = await StartReplicaAsync(instance, runs);
        await using var two = await StartReplicaAsync(instance, runs);
        using var first = await PostAsync(one, "tea", "retry-1");

        // Act
        using var second = await PostAsync(two, "tea", "retry-1");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(runs[0]).IsEqualTo(1);
            await Assert.That(first.StatusCode).IsEqualTo(HttpStatusCode.Created);
            await Assert.That(first.Headers.Contains("Idempotent-Replayed")).IsFalse();
            await Assert.That(second.StatusCode).IsEqualTo(HttpStatusCode.Created);
            await Assert.That(second.Headers.GetValues("Idempotent-Replayed")).IsEquivalentTo(["true"]);
            await Assert.That(await second.Content.ReadAsStringAsync()).IsEqualTo(await first.Content.ReadAsStringAsync());
        }
    }
}
