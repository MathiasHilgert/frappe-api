using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Alba;
using Frappe.Geo.Infrastructure;
using Frappe.TestUtilities;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using NodaTime.TimeZones;
using Npgsql;

namespace Frappe.Geo.IntegrationTests.Timezones;

public sealed class ListTimezonesTests(AppFixture app)
{
    private const string Url = "/v1/geo/timezones";

    // Fails every later read of the catalogue, as a database outage would.
    private const string RevokeRead = "REVOKE SELECT ON geo.timezones FROM frappe_app";

    // A zone a later release retired is kept, so the rows differ from what this host's release lists.
    private const string RetireZone =
        "INSERT INTO geo.timezones (name, canonical_name) VALUES ('Test/Retired', NULL)";

    // The shortest snapshot life the options accept, so a test can outlive it.
    private static TimeSpan ShortMaxAge => TimeSpan.FromSeconds(1);

    // Bounds a wait that a regression would otherwise hang.
    private static TimeSpan Patience => TimeSpan.FromSeconds(30);

    [Fact]
    public async Task The_list_is_one_page_of_every_zone_ordered_by_id()
    {
        IScenarioResult result = await app.Host.Scenario(static scenario =>
        {
            scenario.Get.Url(Url);
            scenario.StatusCodeShouldBeOk();
        });

        using var body = JsonDocument.Parse(await result.ReadAsTextAsync());
        JsonElement root = body.RootElement;
        root.EnumerateObject()
            .Select(static member => member.Name)
            .ShouldBe(["items", "nextCursor"]);
        root.GetProperty("nextCursor").ValueKind.ShouldBe(JsonValueKind.Null);
        string?[] ids =
        [
            .. root.GetProperty("items")
                .EnumerateArray()
                .Select(static item => item.GetProperty("id").GetString()),
        ];
        ids.Length.ShouldBe(TzdbDateTimeZoneSource.Default.CanonicalIdMap.Count);
        ids.ShouldBeInOrder(SortDirection.Ascending, StringComparer.Ordinal);
    }

    [Theory]
    [InlineData(
        "America/Cordoba",
        """{"id":"America/Cordoba","canonicalId":"America/Argentina/Cordoba"}"""
    )]
    [InlineData(
        "America/Argentina/Cordoba",
        """{"id":"America/Argentina/Cordoba","canonicalId":null}"""
    )]
    public async Task Each_zone_carries_its_id_and_canonical_id(string id, string json)
    {
        IScenarioResult result = await app.Host.Scenario(static scenario =>
        {
            scenario.Get.Url(Url);
            scenario.StatusCodeShouldBeOk();
        });

        using var body = JsonDocument.Parse(await result.ReadAsTextAsync());
        body.RootElement.GetProperty("items")
            .EnumerateArray()
            .Single(item =>
                string.Equals(item.GetProperty("id").GetString(), id, StringComparison.Ordinal)
            )
            .GetRawText()
            .ShouldBe(json);
    }

    [Fact]
    public async Task The_list_is_publicly_cacheable_and_strongly_tagged()
    {
        IScenarioResult result = await app.Host.Scenario(static scenario =>
        {
            scenario.Get.Url(Url);
            scenario.StatusCodeShouldBeOk();
        });

        result.Context.Response.Headers.ETag.ToString().ShouldMatch("^\"[0-9a-f]{32}\"$");
        result.Context.Response.Headers.CacheControl.ToString().ShouldBe("public, max-age=86400");
        result.Context.Response.Headers.ContainsKey("Vary").ShouldBeFalse();
    }

    [Fact]
    public async Task The_tag_is_a_hash_of_the_json_body_served()
    {
        IScenarioResult result = await app.Host.Scenario(static scenario =>
        {
            scenario.Get.Url(Url);
            scenario.StatusCodeShouldBeOk();
        });

        byte[] body = Encoding.UTF8.GetBytes(await result.ReadAsTextAsync());
        result.Context.Response.ContentType.ShouldBe("application/json; charset=utf-8");
        result
            .Context.Response.Headers.ETag.ToString()
            .ShouldBe($"\"{Convert.ToHexStringLower(SHA256.HashData(body).AsSpan(0, 16))}\"");
    }

    [Theory]
    [InlineData("{0}")]
    [InlineData("W/{0}")]
    [InlineData("\"stale\", {0}")]
    [InlineData("*")]
    public async Task A_matching_If_None_Match_is_answered_304_with_no_body(string ifNoneMatch)
    {
        string etag = await ETagAsync(app.Host);

        IScenarioResult result = await app.Host.Scenario(scenario =>
        {
            scenario.Get.Url(Url);
            scenario.WithRequestHeader(
                "If-None-Match",
                string.Format(CultureInfo.InvariantCulture, ifNoneMatch, etag)
            );
            scenario.StatusCodeShouldBe(304);
        });

        (await result.ReadAsTextAsync()).ShouldBeEmpty();
        result.Context.Response.Headers.ETag.ToString().ShouldBe(etag);
        result.Context.Response.Headers.CacheControl.ToString().ShouldBe("public, max-age=86400");
    }

    [Fact]
    public async Task The_tag_stays_while_the_snapshot_is_fresh()
    {
        await using FrappeDatabase database = new();
        await database.InitializeAsync();
        await using IAlbaHost host = await StartAsync(database);
        string seeded = await ETagAsync(host);

        await ExecuteAsync(database, RetireZone);

        (await ETagAsync(host)).ShouldBe(seeded);
    }

    [Fact]
    public async Task The_tag_follows_the_rows_served_once_the_snapshot_expires()
    {
        await using FrappeDatabase database = new();
        await database.InitializeAsync();
        await using IAlbaHost host = await StartAsync(database, ShortMaxAge);
        string seeded = await ETagAsync(host);

        await ExecuteAsync(database, RetireZone);

        await Task.Delay(ShortMaxAge * 1.5, TestContext.Current.CancellationToken);
        string changed = await ETagAsync(host);
        changed.ShouldNotBe(seeded, StringComparer.Ordinal);
        IScenarioResult stale = await host.Scenario(scenario =>
        {
            scenario.Get.Url(Url);
            scenario.WithRequestHeader("If-None-Match", seeded);
            scenario.StatusCodeShouldBeOk();
        });
        stale.Context.Response.Headers.ETag.ToString().ShouldBe(changed);
    }

    [Fact]
    public async Task A_catalogue_past_the_page_cap_fails_instead_of_truncating()
    {
        await using var database = FrappeDatabase.WithoutSeed();
        await database.InitializeAsync();
        await using IAlbaHost host = await StartAsync(database, ShortMaxAge);
        await ExecuteAsync(
            database,
            "INSERT INTO geo.timezones (name) SELECT 'Test/Zone' || n FROM generate_series(1, 1000) AS n"
        );

        IScenarioResult full = await host.Scenario(static scenario =>
        {
            scenario.Get.Url(Url);
            scenario.StatusCodeShouldBeOk();
        });
        using (var body = JsonDocument.Parse(await full.ReadAsTextAsync()))
        {
            body.RootElement.GetProperty("items").GetArrayLength().ShouldBe(1000);
        }

        await ExecuteAsync(database, "INSERT INTO geo.timezones (name) VALUES ('Test/Zone1001')");
        await Task.Delay(ShortMaxAge * 1.5, TestContext.Current.CancellationToken);

        // A failed refresh is never cacheable, and even a conditional request no longer answers 304.
        IScenarioResult result = await host.Scenario(static scenario =>
        {
            scenario.Get.Url(Url);
            scenario.WithRequestHeader("If-None-Match", "*");
            scenario.StatusCodeShouldBe(500);
        });
        result.Context.Response.Headers.ContainsKey("ETag").ShouldBeFalse();
        result.Context.Response.Headers.ContainsKey("Cache-Control").ShouldBeFalse();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(20)]
    public async Task The_list_reads_once_whatever_its_size_and_not_again_while_fresh(int rows)
    {
        await using var database = FrappeDatabase.WithoutSeed();
        await database.InitializeAsync();
        await ExecuteAsync(
            database,
            $"INSERT INTO geo.timezones (name) SELECT 'Test/Zone' || n FROM generate_series(1, {rows}) AS n"
        );
        CommandCounter commands = new();
        await using IAlbaHost host = await StartAsync(
            database,
            configure: builder =>
                builder.Services.ConfigureDbContext<GeoDbContext>(
                    options => options.AddInterceptors(commands),
                    ServiceLifetime.Singleton
                )
        );

        string etag = await ETagAsync(host);
        int first = commands.Count;
        await ETagAsync(host);
        await host.Scenario(scenario =>
        {
            scenario.Get.Url(Url);
            scenario.WithRequestHeader("If-None-Match", etag);
            scenario.StatusCodeShouldBe(304);
        });

        first.ShouldBeLessThanOrEqualTo(1);
        first.ShouldBeGreaterThan(0);
        commands.Count.ShouldBe(first);
    }

    [Fact]
    public async Task Concurrent_requests_on_a_cold_snapshot_read_the_catalogue_once()
    {
        await using var database = FrappeDatabase.WithoutSeed();
        await database.InitializeAsync();
        await ExecuteAsync(database, "INSERT INTO geo.timezones (name) VALUES ('Test/Zone')");
        CommandCounter commands = new();
        await using IAlbaHost host = await StartAsync(
            database,
            configure: builder =>
                builder.Services.ConfigureDbContext<GeoDbContext>(
                    options => options.AddInterceptors(commands),
                    ServiceLifetime.Singleton
                )
        );

        string[] etags = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => ETagAsync(host)));

        etags.Distinct(StringComparer.Ordinal).Count().ShouldBe(1);
        commands.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_failed_refresh_serves_the_snapshot_it_had_with_its_tag()
    {
        await using FrappeDatabase database = new();
        await database.InitializeAsync();
        await using IAlbaHost host = await StartAsync(database, ShortMaxAge);
        IScenarioResult fresh = await host.Scenario(static scenario =>
        {
            scenario.Get.Url(Url);
            scenario.StatusCodeShouldBeOk();
        });
        string body = await fresh.ReadAsTextAsync();
        string etag = fresh.Context.Response.Headers.ETag.ToString();

        await ExecuteAsync(database, RevokeRead);
        await Task.Delay(ShortMaxAge * 1.5, TestContext.Current.CancellationToken);

        IScenarioResult stale = await host.Scenario(static scenario =>
        {
            scenario.Get.Url(Url);
            scenario.StatusCodeShouldBeOk();
        });
        (await stale.ReadAsTextAsync()).ShouldBe(body);
        stale.Context.Response.Headers.ETag.ToString().ShouldBe(etag);
        await host.Scenario(scenario =>
        {
            scenario.Get.Url(Url);
            scenario.WithRequestHeader("If-None-Match", etag);
            scenario.StatusCodeShouldBe(304);
        });
    }

    [Fact]
    public async Task A_refresh_that_fails_with_a_wrapped_database_error_serves_the_snapshot_it_had()
    {
        await using FrappeDatabase database = new();
        await database.InitializeAsync();
        FailingRead failing = new();
        await using IAlbaHost host = await StartAsync(
            database,
            ShortMaxAge,
            builder =>
                builder.Services.ConfigureDbContext<GeoDbContext>(
                    options => options.AddInterceptors(failing),
                    ServiceLifetime.Singleton
                )
        );
        string etag = await ETagAsync(host);

        failing.Start();
        await Task.Delay(ShortMaxAge * 1.5, TestContext.Current.CancellationToken);

        (await ETagAsync(host)).ShouldBe(etag);
        await host.Scenario(scenario =>
        {
            scenario.Get.Url(Url);
            scenario.WithRequestHeader("If-None-Match", etag);
            scenario.StatusCodeShouldBe(304);
        });
    }

    [Fact]
    public async Task A_failed_refresh_is_not_retried_by_every_request()
    {
        await using FrappeDatabase database = new();
        await database.InitializeAsync();
        CommandCounter commands = new();
        await using IAlbaHost host = await StartAsync(
            database,
            ShortMaxAge,
            builder =>
                builder.Services.ConfigureDbContext<GeoDbContext>(
                    options => options.AddInterceptors(commands),
                    ServiceLifetime.Singleton
                )
        );
        await ETagAsync(host);

        await ExecuteAsync(database, RevokeRead);
        await Task.Delay(ShortMaxAge * 1.5, TestContext.Current.CancellationToken);
        for (int i = 0; i < 5; i++)
        {
            await ETagAsync(host);
        }

        commands.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_cold_snapshot_with_the_database_failing_is_an_uncacheable_503()
    {
        await using FrappeDatabase database = new();
        await database.InitializeAsync();
        await ExecuteAsync(database, RevokeRead);
        await using IAlbaHost host = await StartAsync(database);

        // No snapshot can validate a tag, so even a matching If-None-Match gets no 304.
        IScenarioResult result = await host.Scenario(static scenario =>
        {
            scenario.Get.Url(Url);
            scenario.WithRequestHeader("If-None-Match", "*");
            scenario.StatusCodeShouldBe(503);
        });

        result.Context.Response.ContentType.ShouldBe("application/problem+json");
        result.Context.Response.Headers.RetryAfter.ToString().ShouldBe("5");
        result.Context.Response.Headers.CacheControl.ToString().ShouldBe("no-store");
        result.Context.Response.Headers.ContainsKey("ETag").ShouldBeFalse();
    }

    [Fact]
    public async Task A_snapshot_built_after_a_failure_still_expires_on_its_max_age()
    {
        await using FrappeDatabase database = new();
        await database.InitializeAsync();
        await ExecuteAsync(database, RevokeRead);
        await using IAlbaHost host = await StartAsync(database, ShortMaxAge);
        await host.Scenario(static scenario =>
        {
            scenario.Get.Url(Url);
            scenario.StatusCodeShouldBe(503);
        });
        await ExecuteAsync(database, "GRANT SELECT ON geo.timezones TO frappe_app");
        string recovered = await ETagAsync(host);

        await ExecuteAsync(database, RetireZone);
        await Task.Delay(ShortMaxAge * 1.5, TestContext.Current.CancellationToken);

        (await ETagAsync(host)).ShouldNotBe(recovered, StringComparer.Ordinal);
    }

    [Fact]
    public async Task A_cold_catalogue_past_the_page_cap_is_still_a_500()
    {
        await using var database = FrappeDatabase.WithoutSeed();
        await database.InitializeAsync();
        await ExecuteAsync(
            database,
            "INSERT INTO geo.timezones (name) SELECT 'Test/Zone' || n FROM generate_series(1, 1001) AS n"
        );
        await using IAlbaHost host = await StartAsync(database);

        IScenarioResult result = await host.Scenario(static scenario =>
        {
            scenario.Get.Url(Url);
            scenario.StatusCodeShouldBe(500);
        });

        result.Context.Response.Headers.ContainsKey("Retry-After").ShouldBeFalse();
        result.Context.Response.Headers.ContainsKey("Cache-Control").ShouldBeFalse();
    }

    [Fact]
    public async Task A_caller_that_cancels_does_not_cancel_the_refresh_another_caller_waits_on()
    {
        await using var database = FrappeDatabase.WithoutSeed();
        await database.InitializeAsync();
        await ExecuteAsync(database, "INSERT INTO geo.timezones (name) VALUES ('Test/Zone')");
        CommandCounter commands = new();
        HeldRead held = new();
        await using IAlbaHost host = await StartAsync(
            database,
            configure: builder =>
                builder.Services.ConfigureDbContext<GeoDbContext>(
                    options => options.AddInterceptors(commands, held),
                    ServiceLifetime.Singleton
                )
        );
        using HttpClient client = host.Server.CreateClient();
        using CancellationTokenSource first = new();

        Task<HttpResponseMessage> cancelled = client.GetAsync(Url, first.Token);
        await held.Entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        Task<HttpResponseMessage> waiting = client.GetAsync(
            Url,
            TestContext.Current.CancellationToken
        );
        await first.CancelAsync();
        held.Release.SetResult();

        using HttpResponseMessage response = await waiting.WaitAsync(
            Patience,
            TestContext.Current.CancellationToken
        );
        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
        commands.Count.ShouldBe(1);
        await Should.ThrowAsync<OperationCanceledException>(
            cancelled.WaitAsync(Patience, TestContext.Current.CancellationToken)
        );
    }

    private static async Task<string> ETagAsync(IAlbaHost host)
    {
        IScenarioResult result = await host.Scenario(static scenario =>
        {
            scenario.Get.Url(Url);
            scenario.StatusCodeShouldBeOk();
        });
        return result.Context.Response.Headers.ETag.ToString();
    }

    private static async Task ExecuteAsync(FrappeDatabase database, string sql)
    {
        await using NpgsqlConnection connection = new(database.OwnerConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using NpgsqlCommand command = new(sql, connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private Task<IAlbaHost> StartAsync(
        FrappeDatabase database,
        TimeSpan? maxAge = null,
        Action<WebApplicationBuilder>? configure = null
    ) =>
        FrappeTestHost.StartAsync(
            database.AppConnectionString,
            [new GeoModule()],
            builder =>
            {
                if (maxAge is { } age)
                {
                    builder.Configuration[$"{GeoOptions.SectionName}:CatalogueMaxAge"] =
                        age.ToString("c", CultureInfo.InvariantCulture);
                }

                configure?.Invoke(builder);
            },
            nats: NatsScope.On(app.Nats)
        );

    // Fails every read once set, with an error that is no DbException, as a wrapped database failure would be.
    private sealed class FailingRead : DbCommandInterceptor
    {
        private volatile bool _failing;

        public void Start() => _failing = true;

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        ) =>
            _failing
                ? throw new TimeoutException("The database did not answer.")
                : ValueTask.FromResult(result);
    }

    // Holds the first read until the test releases it, so a caller can cancel while the refresh is in flight.
    private sealed class HeldRead : DbCommandInterceptor
    {
        private int _reads;

        public TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            if (Interlocked.Increment(ref _reads) == 1)
            {
                Entered.SetResult();
                await Release.Task;
            }

            return result;
        }
    }
}
