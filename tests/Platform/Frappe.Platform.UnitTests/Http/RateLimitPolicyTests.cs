using System.Collections.Concurrent;
using System.Net;
using Frappe.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Frappe.Platform.UnitTests.Http;

/// <summary>
/// A named per-client policy next to the anonymous one: its own limit and budget, bound from its own section, counted
/// in the shared store under its own name.
/// </summary>
public sealed class RateLimitPolicyTests
{
    private const string Policy = "strict";
    private const string Section = "Test:Strict";

    /// <summary>A rate-limit store that remembers the keys it was asked about and allows every request.</summary>
    private sealed class RecordingStore : IRateLimitStore
    {
        private readonly ConcurrentDictionary<string, long> _counts = new();

        public ConcurrentBag<string> Keys { get; } = [];

        public ValueTask<RateLimitWindow> HitAsync(string key, int permits, TimeSpan window, CancellationToken cancellationToken)
        {
            Keys.Add(key);
            return ValueTask.FromResult(new RateLimitWindow(_counts.AddOrUpdate(key, permits, (_, count) => count + permits), window));
        }
    }

    private static async Task<WebApplication> StartAsync(IDictionary<string, string?>? settings = null, IRateLimitStore? store = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(settings ?? new Dictionary<string, string?>());
        builder.Services
            .AddFrappeHttp()
            .AddFrappeRateLimitPolicy(Policy, Section, new AnonymousRateLimit(2, TimeSpan.FromMinutes(10)));
        if (store is not null)
        {
            builder.Services.AddSingleton(store);
        }

        var app = builder.Build();
        app.UseFrappeHttp();
        var v1 = app.MapGroup("/v1").RequireRateLimiting(HttpRegistration.AnonymousPolicy);
        v1.MapGet("/ping", () => "pong");
        v1.MapGet("/strict", () => "strict").RequireRateLimiting(Policy);
        await app.StartAsync();
        return app;
    }

    private static async Task<HttpResponseMessage> GetAsync(WebApplication app, string path)
    {
        using var client = app.GetTestClient();
        return await client.GetAsync(new Uri(path, UriKind.Relative));
    }

    [Test]
    public async Task A_named_policy_has_its_own_limit_apart_from_the_anonymous_one()
    {
        // Arrange
        await using var app = await StartAsync();

        using var first = await GetAsync(app, "/v1/strict");
        using var second = await GetAsync(app, "/v1/strict");
        using var third = await GetAsync(app, "/v1/strict");

        // Act
        using var other = await GetAsync(app, "/v1/ping");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(first.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(second.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(third.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
            await Assert.That(third.Headers.RetryAfter?.Delta).IsNotNull();
            await Assert.That(other.StatusCode).IsEqualTo(HttpStatusCode.OK);
        }
    }

    [Test]
    public async Task Its_section_overrides_the_defaults()
    {
        // Arrange
        await using var app = await StartAsync(new Dictionary<string, string?> { [$"{Section}:PermitLimit"] = "1" });

        using var first = await GetAsync(app, "/v1/strict");

        // Act
        using var second = await GetAsync(app, "/v1/strict");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(first.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(second.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
        }
    }

    [Test]
    [Arguments("PermitLimit", "0", "must be at least 1")]
    [Arguments("Window", "00:00:00", "must be positive")]
    public async Task An_invalid_limit_fails_at_startup_naming_its_key(string key, string value, string rule)
    {
        // Act
        var failure = await Assert.ThrowsAsync<OptionsValidationException>(async () => await StartAsync(new Dictionary<string, string?> { [$"{Section}:{key}"] = value }));

        // Assert
        await Assert.That(failure!.Message).Contains($"Configuration '{Section}:{key}' {rule}.");
    }

    [Test]
    public async Task The_shared_store_counts_it_under_its_own_name()
    {
        // Arrange
        var store = new RecordingStore();
        await using var app = await StartAsync(store: store);

        using var strict = await GetAsync(app, "/v1/strict");

        // Act
        using var other = await GetAsync(app, "/v1/ping");

        // Assert
        await Assert.That(store.Keys.Order()).IsEquivalentTo([$"{HttpRegistration.AnonymousPolicy}:unknown", $"{Policy}:unknown"]);
    }

    [Test]
    public async Task The_anonymous_policy_name_cannot_be_taken()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var failure = Assert.Throws<ArgumentException>(() => services.AddFrappeRateLimitPolicy(HttpRegistration.AnonymousPolicy, Section, new AnonymousRateLimit(1, TimeSpan.FromMinutes(1))));

        // Assert
        await Assert.That(failure).IsNotNull();
    }
}
