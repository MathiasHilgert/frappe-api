using System.Security.Cryptography;
using System.Text;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Images;
using DotNet.Testcontainers.Networks;
using Frappe.Testing;
using Testcontainers.PostgreSql;
using TUnit.Core.Interfaces;

namespace Frappe.EndToEnd.Tests;

/// <summary>
/// What gets deployed, once per test session: the image built from <c>deploy/Dockerfile</c>, its infrastructure
/// (Postgres, NATS, Valkey), then the Migrator, the Api and the Worker as containers, wired exactly like <c>deploy/compose.yaml</c> wires them. Passwords are throwaway values generated here.
/// </summary>
public sealed class DeployedFrappe : IAsyncInitializer, IAsyncDisposable
{
    // Pinned, at least two weeks old: the CLI that drives BuildKit.
    private const string DockerCliImage = "docker:29.9.2-cli";

    // The same images deploy/compose.yaml runs.
    private const string PostgresImage = "postgis/postgis:18-3.7-alpine";
    private const string NatsImage = "nats:2.11-alpine";
    private const string ValkeyImage = "valkey/valkey:8-alpine";

    /// <summary>Every wait gives up instead of hanging the session (Testcontainers waits forever by default).</summary>
    private static readonly Action<IWaitStrategy> Bounded = wait => wait.WithTimeout(TimeSpan.FromMinutes(2));

    private readonly string _ownerPassword = Secret();
    private readonly string _geoPassword = Secret();
    private readonly string _schedulerPassword = Secret();
    private readonly List<IContainer> _containers = [];

    private INetwork? _network;
    private IContainer? _postgres;
    private IFutureDockerImage? _image;
    private IContainer? _migrator;
    private IContainer? _api;
    private IContainer? _worker;

    public long MigratorExitCode { get; private set; } = -1;

    public HttpClient Api { get; private set; } = null!;

    /// <summary>The database server, for a test that must seed what no source can provide without reaching the internet.</summary>
    public IContainer Postgres => _postgres ?? throw new InvalidOperationException("Postgres has not started.");

    public IContainer Worker => _worker ?? throw new InvalidOperationException("The Worker has not started.");

    public IReadOnlyList<IContainer> Hosts => [.. new[] { _migrator, _api, _worker }.OfType<IContainer>()];

    public async Task InitializeAsync()
    {
        _network = new NetworkBuilder().WithName("frappe-e2e-" + Guid.NewGuid().ToString("N")[..12]).Build();
        await _network.CreateAsync();

        _image = new BuildKitImageFromDockerfileBuilder(DockerCliImage)
            .WithContextDirectory(RepositoryRoot.FullPath)
            .WithDockerfileDirectory(Path.Combine(RepositoryRoot.FullPath, "deploy"))
            .WithDockerfile("Dockerfile")
            .WithName("localhost/frappe-e2e:" + Guid.NewGuid().ToString("N"))
            .WithCleanUp(true)
            .Build();

        var postgres = Track(new PostgreSqlBuilder(PostgresImage)
            .WithNetwork(_network).WithNetworkAliases("postgres")
            .WithDatabase("frappe").WithUsername("frappe_owner").WithPassword(_ownerPassword)
            .Build());
        _postgres = postgres;
        var nats = Track(new ContainerBuilder(NatsImage)
            .WithNetwork(_network).WithNetworkAliases("nats")
            .WithCommand("--jetstream", "--http_port", "8222")
            .WithPortBinding(8222, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(8222).ForPath("/healthz"), Bounded))
            .Build());
        var valkey = Track(new ContainerBuilder(ValkeyImage)
            .WithNetwork(_network).WithNetworkAliases("valkey")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilCommandIsCompleted(["valkey-cli", "ping"], Bounded))
            .Build());
        // The image build is the long part: the infrastructure starts meanwhile.
        await Task.WhenAll(_image.CreateAsync(), postgres.StartAsync(), nats.StartAsync(), valkey.StartAsync());

        await RunMigratorAsync();

        _api = Track(new ContainerBuilder(_image)
            .WithNetwork(_network)
            .WithEnvironment("ConnectionStrings__Geo", GeoConnectionString())
            .WithEnvironment("ConnectionStrings__Valkey", "valkey:6379")
            .WithPortBinding(8080, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(8080).ForPath("/health/live"), Bounded))
            .Build());
        _worker = Track(new ContainerBuilder(_image)
            .WithNetwork(_network)
            .WithEntrypoint("/app/worker/Frappe.Worker")
            .WithEnvironment("ConnectionStrings__Geo", GeoConnectionString())
            .WithEnvironment("ConnectionStrings__Scheduler", $"Host=postgres;Database=frappe;Username=frappe_scheduler;Password={_schedulerPassword}")
            .WithEnvironment("ConnectionStrings__Valkey", "valkey:6379")
            .WithEnvironment("ConnectionStrings__Nats", "nats://nats:4222")
            // Never a real source from a test: the territory sync at startup fails against a reserved name, is audited and
            // retried on its schedule, and the Worker stays ready.
            .WithEnvironment("Geo__Argentina__Georef__BaseUrl", "http://georef.invalid/")
            .WithEnvironment("Geo__Argentina__Holidays__BaseUrl", "http://holidays.invalid/")
            .WithEnvironment("Geo__Argentina__ProvincialHolidays__BaseUrl", "http://calendarios.invalid/")
            .Build());

        try
        {
            await Task.WhenAll(_api.StartAsync(), _worker.StartAsync());
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException("The Api or the Worker did not start." + await LogsAsync(), exception);
        }

        Api = new HttpClient { BaseAddress = new UriBuilder("http", _api.Hostname, _api.GetMappedPublicPort(8080)).Uri };
    }

    /// <summary>The one-shot Migrator: roles, migrations and Quartz tables. The Api and the Worker wait for it.</summary>
    private async Task RunMigratorAsync()
    {
        _migrator = Track(new ContainerBuilder(_image!)
            .WithNetwork(_network!)
            .WithEntrypoint("/app/migrator/Frappe.Migrator")
            .WithEnvironment("ConnectionStrings__Owner", $"Host=postgres;Database=frappe;Username=frappe_owner;Password={_ownerPassword}")
            .WithEnvironment("Roles__Geo__Password", _geoPassword)
            .WithEnvironment("Roles__Scheduler__Password", _schedulerPassword)
            .Build());
        try
        {
            await _migrator.StartAsync();
        }
        catch (ContainerNotRunningException)
        {
            // Finished before Testcontainers saw it running: its exit code below says how.
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        MigratorExitCode = await _migrator.GetExitCodeAsync(timeout.Token);
        if (MigratorExitCode != 0)
        {
            throw new InvalidOperationException($"The Migrator exited with {MigratorExitCode}." + await LogsAsync());
        }
    }

    /// <summary>The Migrator's, the Api's and the Worker's output, to explain a failure.</summary>
    public async Task<string> LogsAsync()
    {
        var text = new StringBuilder();
        foreach (var (name, container) in new[] { ("migrator", _migrator), ("api", _api), ("worker", _worker) })
        {
            if (container is null)
            {
                continue;
            }

            try
            {
                var (stdout, stderr) = await container.GetLogsAsync(timestampsEnabled: false);
                text.Append(System.Globalization.CultureInfo.InvariantCulture, $"\n\n===== {name} (stdout) =====\n{stdout}\n===== {name} (stderr) =====\n{stderr}");
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                text.Append(System.Globalization.CultureInfo.InvariantCulture, $"\n\n===== {name}: logs unavailable ({exception.Message}) =====");
            }
        }

        return text.ToString();
    }

    private string GeoConnectionString() => $"Host=postgres;Database=frappe;Username=frappe_geo;Password={_geoPassword}";

    private T Track<T>(T container)
        where T : IContainer
    {
        _containers.Add(container);
        return container;
    }

    private static string Secret() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    public async ValueTask DisposeAsync()
    {
        Api?.Dispose();
        foreach (var container in Enumerable.Reverse(_containers))
        {
            await container.DisposeAsync();
        }

        if (_image is not null)
        {
            await _image.DisposeAsync();
        }

        if (_network is not null)
        {
            await _network.DisposeAsync();
        }
    }
}
