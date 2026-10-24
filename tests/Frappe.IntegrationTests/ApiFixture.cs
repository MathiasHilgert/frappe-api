using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Alba;
using Frappe.Identity.Application.Security;
using Frappe.Identity.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.Nats;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using Wolverine;

[assembly: AssemblyFixture(typeof(Frappe.IntegrationTests.ApiFixture))]

namespace Frappe.IntegrationTests;

/// <summary>Boots the real API against throwaway Postgres, NATS (JetStream) and Valkey containers.</summary>
public sealed class ApiFixture : IAsyncLifetime
{
    public const string SessionCookieName = "__Host-frappe_session";

    /// <summary>
    /// Local opt-in (<c>TESTCONTAINERS_REUSE_ENABLE=true</c>): keep the containers between runs to skip their
    /// startup. Never in CI, where every run must start clean.
    /// </summary>
    private static readonly bool Reuse =
        Environment.GetEnvironmentVariable("TESTCONTAINERS_REUSE_ENABLE") == "true"
        && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"));

    /// <summary>
    /// Reused containers are found by label; the checkout path keeps two worktrees from sharing (and flushing)
    /// each other's containers.
    /// </summary>
    private static readonly string ReuseScope = Convert.ToHexStringLower(
        SHA256.HashData(Encoding.UTF8.GetBytes(AppContext.BaseDirectory)))[..12];

    // Parallel collections each boot hosts with their own pools, and burst tests fire requests at once: the default
    // 100 connections run out.
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithCommand("-c", "max_connections=500")
        .WithReuse(Reuse).WithLabel("reuse-id", $"frappe-integration-postgres-{ReuseScope}").Build();
    private readonly NatsContainer _nats = new NatsBuilder("nats:2-alpine").WithCommand("--jetstream")
        .WithReuse(Reuse).WithLabel("reuse-id", $"frappe-integration-nats-{ReuseScope}").Build();
    private readonly RedisContainer _valkey = new RedisBuilder("valkey/valkey:8-alpine")
        .WithCommand("--databases", ValkeyDatabases.ToString(System.Globalization.CultureInfo.InvariantCulture))
        .WithReuse(Reuse).WithLabel("reuse-id", $"frappe-integration-valkey-{ReuseScope}").Build();

    /// <summary>Logical databases on the Valkey container: one per collection, never shared.</summary>
    private const int ValkeyDatabases = 256;

    private readonly ConcurrentDictionary<string, Lazy<Task<CollectionStack>>> _stacks = new(StringComparer.Ordinal);
    private int _nextStack;

    /// <summary>
    /// The host of the running test collection. Collections run in parallel, so each one gets its own database
    /// (its own Wolverine node and schema) and Valkey logical database, reused by every test of that collection.
    /// The containers are shared.
    /// </summary>
    public IAlbaHost Host => BootedStack.Host;

    /// <summary>The running collection's database, migrated, where its <see cref="Host"/> booted.</summary>
    public string PostgresConnectionString => BootedStack.ConnectionString;

    private static string CollectionKey => TestContext.Current.TestCollection?.UniqueID ?? string.Empty;

    /// <summary>
    /// The synchronous <see cref="Host"/> boots the collection on first use: the boot runs on the thread pool,
    /// away from the test's synchronization context, so waiting on it cannot deadlock; later reads are instant.
    /// </summary>
    private CollectionStack BootedStack
    {
        get
        {
            if (_stacks.TryGetValue(CollectionKey, out var booted) && booted.IsValueCreated
                && booted.Value.IsCompletedSuccessfully)
            {
                return booted.Value.Result;
            }

            return Task.Run(StackAsync).GetAwaiter().GetResult();
        }
    }

    /// <summary>Boots the running collection's database and host once; later calls reuse them.</summary>
    private Task<CollectionStack> StackAsync() =>
        _stacks.GetOrAdd(CollectionKey, _ => new Lazy<Task<CollectionStack>>(CreateStackAsync)).Value;

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _nats.StartAsync(), _valkey.StartAsync());
        // Attempt limiter budgets from the previous run would answer 429; this container is the suite's own.
        if (Reuse)
            await _valkey.ExecAsync(["valkey-cli", "FLUSHALL"]);
    }

    private async Task<CollectionStack> CreateStackAsync()
    {
        var index = Interlocked.Increment(ref _nextStack);
        if (index >= ValkeyDatabases)
            throw new InvalidOperationException($"More than {ValkeyDatabases - 1} collections; raise ValkeyDatabases.");
        var connectionString = await CreateDatabaseAsync($"collection_{index}");
        var valkey = $"{_valkey.GetConnectionString()},defaultDatabase={index}";
        // Every later host of the collection, whatever its environment, finds the schema in place: this
        // Development host provisions Wolverine's storage on start, then the module schemas are migrated.
        var host = await CreateHostAsync(connectionString: connectionString, valkeyConnectionString: valkey);
        try
        {
            await using var scope = host.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
        }
        catch
        {
            await host.DisposeAsync();
            throw;
        }

        return new CollectionStack(host, connectionString, valkey);
    }

    /// <summary>One collection's migrated database, Valkey database and shared host.</summary>
    private sealed record CollectionStack(IAlbaHost Host, string ConnectionString, string ValkeyConnectionString);

    /// <summary>Boots an extra host on the same containers, e.g. with test-only services or another database.</summary>
    public async Task<IAlbaHost> CreateHostAsync(
        Action<IServiceCollection>? configureServices = null,
        string environment = "Development",
        string? connectionString = null,
        bool realPasswordHasher = false,
        Action<IDictionary<string, string?>>? configureSettings = null)
    {
        var stack = await StackAsync();
        return await CreateHostAsync(configureServices, environment, connectionString ?? stack.ConnectionString,
            stack.ValkeyConnectionString, realPasswordHasher, configureSettings);
    }

    private Task<IAlbaHost> CreateHostAsync(
        Action<IServiceCollection>? configureServices = null,
        string environment = "Development",
        string? connectionString = null,
        string? valkeyConnectionString = null,
        bool realPasswordHasher = false,
        Action<IDictionary<string, string?>>? configureSettings = null) =>
        AlbaHost.For<Program>(builder =>
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("ConnectionStrings:frappe", connectionString);
            builder.UseSetting("ConnectionStrings:nats", _nats.GetConnectionString());
            builder.UseSetting("ConnectionStrings:valkey", valkeyConnectionString);
            builder.UseSetting("Identity:SecretHashing:Key", "integration-tests-secret-hashing-key-0123456789");
            builder.UseSetting("Identity:SessionCookie:Name", SessionCookieName);
            builder.UseSetting("Identity:SessionCookie:Path", "/");
            // No test reaches the real HaveIBeenPwned: a closed local port makes the check fail open at once.
            builder.UseSetting("Identity:BreachedPasswords:BaseAddress", "http://127.0.0.1:9/");
            // A developer's user-secrets may hold a real Resend key; tests never send mail.
            builder.UseSetting("Notification:Resend:ApiKey", string.Empty);
            // A generous limiter budget so a busy CI container never makes the real-Valkey tests fail open.
            builder.UseSetting("Identity:AttemptLimiter:TimeoutMilliseconds", "2000");
            // Load the committed handler code like production does: a stale `just codegen` fails here, not in prod.
            builder.UseSetting("Wolverine:TypeLoadMode", "Static");
            var settings = new Dictionary<string, string?>(StringComparer.Ordinal);
            configureSettings?.Invoke(settings);
            foreach (var (key, value) in settings)
                builder.UseSetting(key, value);
            builder.ConfigureServices(services =>
            {
                services.RunWolverineInSoloMode();
                // Argon2id dominates the suite's runtime; only the dedicated hashing tests pay for it.
                if (!realPasswordHasher)
                    services.AddSingleton<IPasswordHasher, FastPasswordHasher>();
                configureServices?.Invoke(services);
            });
        });

    /// <summary>Creates an empty database on the container, for tests that must start without any schema.</summary>
    public async Task<string> CreateDatabaseAsync(string name)
    {
        await using (var dataSource = NpgsqlDataSource.Create(_postgres.GetConnectionString()))
        {
            // A reused container keeps the database from the previous run.
            await using (var drop = dataSource.CreateCommand($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)"))
                await drop.ExecuteNonQueryAsync();
            await using (var command = dataSource.CreateCommand($"CREATE DATABASE \"{name}\""))
                await command.ExecuteNonQueryAsync();
        }

        return new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Database = name }.ConnectionString;
    }

    public async ValueTask DisposeAsync()
    {
        // Hosts that never booted have nothing to dispose; disposing must not mask a startup error.
        foreach (var stack in _stacks.Values.Where(s => s.IsValueCreated && s.Value.IsCompletedSuccessfully))
            await (await stack.Value).Host.DisposeAsync();
        // Testcontainers leaves reused containers running on dispose (Ryuk skips them).
        await Task.WhenAll(_postgres.DisposeAsync().AsTask(), _nats.DisposeAsync().AsTask(), _valkey.DisposeAsync().AsTask());
    }
}
