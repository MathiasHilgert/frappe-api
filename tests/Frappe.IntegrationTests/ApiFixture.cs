using Alba;
using Frappe.Identity.Application.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Wolverine;
using Testcontainers.Nats;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

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

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithReuse(Reuse).WithLabel("reuse-id", "frappe-integration-postgres").Build();
    private readonly NatsContainer _nats = new NatsBuilder("nats:2-alpine").WithCommand("--jetstream")
        .WithReuse(Reuse).WithLabel("reuse-id", "frappe-integration-nats").Build();
    private readonly RedisContainer _valkey = new RedisBuilder("valkey/valkey:8-alpine")
        .WithReuse(Reuse).WithLabel("reuse-id", "frappe-integration-valkey").Build();

    public IAlbaHost Host { get; private set; } = null!;

    public string PostgresConnectionString => _postgres.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _nats.StartAsync(), _valkey.StartAsync());
        // Attempt limiter budgets from the previous run would answer 429; this container is the suite's own.
        if (Reuse)
            await _valkey.ExecAsync(["valkey-cli", "FLUSHALL"]);
        Host = await CreateHostAsync();
    }

    /// <summary>Boots an extra host on the same containers, e.g. with test-only services or another database.</summary>
    public Task<IAlbaHost> CreateHostAsync(
        Action<IServiceCollection>? configureServices = null,
        string environment = "Development",
        string? connectionString = null,
        bool realPasswordHasher = false) =>
        AlbaHost.For<Program>(builder =>
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("ConnectionStrings:frappe", connectionString ?? _postgres.GetConnectionString());
            builder.UseSetting("ConnectionStrings:nats", _nats.GetConnectionString());
            builder.UseSetting("ConnectionStrings:valkey", _valkey.GetConnectionString());
            builder.UseSetting("Identity:SecretHashing:Key", "integration-tests-secret-hashing-key-0123456789");
            builder.UseSetting("Identity:SessionCookie:Name", SessionCookieName);
            builder.UseSetting("Identity:SessionCookie:Path", "/");
            // No test reaches the real HaveIBeenPwned: a closed local port makes the check fail open at once.
            builder.UseSetting("Identity:BreachedPasswords:BaseAddress", "http://127.0.0.1:9/");
            // A developer's user-secrets may hold a real Resend key; tests never send mail.
            builder.UseSetting("Notification:Resend:ApiKey", string.Empty);
            // A generous limiter budget so a busy CI container never makes the real-Valkey tests fail open.
            builder.UseSetting("Identity:AttemptLimiter:TimeoutMilliseconds", "2000");
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
        await using (var dataSource = NpgsqlDataSource.Create(PostgresConnectionString))
        {
            // A reused container keeps the database from the previous run.
            await using (var drop = dataSource.CreateCommand($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)"))
                await drop.ExecuteNonQueryAsync();
            await using (var command = dataSource.CreateCommand($"CREATE DATABASE \"{name}\""))
                await command.ExecuteNonQueryAsync();
        }

        return new NpgsqlConnectionStringBuilder(PostgresConnectionString) { Database = name }.ConnectionString;
    }

    public async ValueTask DisposeAsync()
    {
        // Host stays unset when startup failed; disposing must not mask that error.
        if (Host is not null)
            await Host.DisposeAsync();

        // Testcontainers leaves reused containers running on dispose (Ryuk skips them).
        await Task.WhenAll(_postgres.DisposeAsync().AsTask(), _nats.DisposeAsync().AsTask(), _valkey.DisposeAsync().AsTask());
    }
}
