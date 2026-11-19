using System.Net;
using Frappe.Caching;
using Frappe.Http;
using Frappe.Platform.IntegrationTests.Caching;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Frappe.Platform.IntegrationTests.Http;

/// <summary>The rate limit windows counted in a real Valkey, shared by every replica that points at it.</summary>
[ClassDataSource<ValkeyServer>(Shared = SharedType.PerTestSession)]
public sealed class ValkeyRateLimitTests(ValkeyServer server)
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    private static string NewInstanceName() => $"ratelimit-{Guid.NewGuid():N}";

    private static async Task<WebApplication> StartReplicaAsync(string connectionString, string instanceName, int permitLimit)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Http:RateLimit:PermitLimit"] = permitLimit.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Http:RateLimit:Window"] = "00:10:00",
        });
        builder.Services
            .AddFrappeCaching(connectionString, instanceName)
            .AddFrappeHttp();

        var app = builder.Build();
        app.UseFrappeHttp();
        app.MapGroup("/v1").RequireRateLimiting(HttpRegistration.AnonymousPolicy).MapGet("/ping", () => "pong");
        await app.StartAsync();
        return app;
    }

    private static async Task<HttpStatusCode> PingAsync(WebApplication app)
    {
        using var response = await app.GetTestClient().GetAsync(new Uri("/v1/ping", UriKind.Relative));
        return response.StatusCode;
    }

    [Test]
    public async Task The_store_counts_hits_in_one_window_that_closes_after_its_length()
    {
        // Arrange
        await using var app = await StartReplicaAsync(server.ConnectionString, NewInstanceName(), permitLimit: 5);
        var store = app.Services.GetRequiredService<IRateLimitStore>();

        // Act
        var first = await store.HitAsync("anonymous:client", 1, Window, CancellationToken.None);
        var second = await store.HitAsync("anonymous:client", 2, Window, CancellationToken.None);
        var other = await store.HitAsync("anonymous:other", 1, Window, CancellationToken.None);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(store).IsTypeOf<ValkeyRateLimitStore>();
            await Assert.That(first.Count).IsEqualTo(1);
            await Assert.That(second.Count).IsEqualTo(3);
            await Assert.That(other.Count).IsEqualTo(1);
            await Assert.That(second.Remaining).IsGreaterThan(TimeSpan.Zero);
            await Assert.That(second.Remaining).IsLessThanOrEqualTo(Window);
        }
    }

    [Test]
    public async Task Replicas_of_one_instance_share_the_client_limit()
    {
        // Arrange
        var instance = NewInstanceName();
        await using var replicaA = await StartReplicaAsync(server.ConnectionString, instance, permitLimit: 2);
        await using var replicaB = await StartReplicaAsync(server.ConnectionString, instance, permitLimit: 2);
        var first = await PingAsync(replicaA);
        var second = await PingAsync(replicaA);

        // Act
        var third = await PingAsync(replicaB);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(first).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(second).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(third).IsEqualTo(HttpStatusCode.TooManyRequests);
        }
    }

    [Test]
    public async Task Instances_with_different_names_count_apart()
    {
        // Arrange
        await using var replicaA = await StartReplicaAsync(server.ConnectionString, NewInstanceName(), permitLimit: 1);
        await using var replicaB = await StartReplicaAsync(server.ConnectionString, NewInstanceName(), permitLimit: 1);

        // Act
        var a = await PingAsync(replicaA);
        var b = await PingAsync(replicaB);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(a).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(b).IsEqualTo(HttpStatusCode.OK);
        }
    }

    [Test]
    public async Task A_valkey_that_cannot_be_reached_leaves_each_replica_its_own_limit()
    {
        // Arrange
        await using var app = await StartReplicaAsync("127.0.0.1:1", NewInstanceName(), permitLimit: 1);
        var first = await PingAsync(app);

        // Act
        var second = await PingAsync(app);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(first).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(second).IsEqualTo(HttpStatusCode.TooManyRequests);
        }
    }
}
