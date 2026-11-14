using Frappe.Geo.Application;
using Frappe.Geo.Application.Territory;
using Frappe.Geo.Countries.Argentina.Territory;
using Frappe.Geo.Infrastructure.Sync;
using Frappe.Geo.Infrastructure.Territory;
using Frappe.Geo.IntegrationTests.Persistence;
using Frappe.Geo.IntegrationTests.Sync;
using Frappe.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NodaTime;

namespace Frappe.Geo.IntegrationTests.Hosts;

/// <summary>What the hosts write to their logs in production: no SQL by default, and a sync's line says only what happened.</summary>
public sealed class HostLoggingTests
{
    [Test]
    [Arguments("src/Hosts/Frappe.Api")]
    [Arguments("src/Hosts/Frappe.Worker")]
    public async Task A_host_logs_SQL_commands_in_development_only(string host)
    {
        var production = Settings(host, "appsettings.json");
        var development = Settings(host, "appsettings.json", "appsettings.Development.json");

        await Assert.That(production["Logging:LogLevel:Microsoft.EntityFrameworkCore.Database.Command"]).IsEqualTo("Warning");
        await Assert.That(development["Logging:LogLevel:Microsoft.EntityFrameworkCore.Database.Command"]).IsEqualTo("Information");
    }

    [Test]
    [ClassDataSource<GeoDatabase>(Shared = SharedType.None)]
    public async Task A_sync_that_succeeds_logs_no_error_and_one_that_fails_logs_its_error(GeoDatabase database)
    {
        var logger = new CapturingLogger<SnapshotSync<TerritorySnapshot, GeorefTerritorySource>>();

        await RunTerritoryAsync(database, new SourceServer { CutBodyOf = "provincias.json" }, logger);
        await RunTerritoryAsync(database, new SourceServer(), logger);

        await Assert.That(logger.Lines.Count).IsEqualTo(2);
        await Assert.That(logger.Lines[0].Level).IsEqualTo(LogLevel.Warning);
        await Assert.That(logger.Lines[0].Message).Contains("Failed");
        await Assert.That(logger.Lines[0].Message).DoesNotContain("(null)");
        await Assert.That(logger.Lines[1].Level).IsEqualTo(LogLevel.Information);
        await Assert.That(logger.Lines[1].Message).DoesNotContain("(null)");
        await Assert.That(logger.Lines[1].Message).EndsWith(")");
    }

    /// <summary>A host's settings files layered as the host loads them, later ones winning.</summary>
    private static IConfigurationRoot Settings(string host, params string[] files)
    {
        var builder = new ConfigurationBuilder();
        foreach (var file in files)
        {
            builder.AddJsonFile(Path.Combine(RepositoryRoot.FullPath, host, file), optional: false);
        }

        return builder.Build();
    }

    private static async Task RunTerritoryAsync(GeoDatabase database, SourceServer georef, ILogger<SnapshotSync<TerritorySnapshot, GeorefTerritorySource>> logger)
    {
        await using var db = database.NewContext();
        await using var provider = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var metrics = new SyncMetrics(new GeoMetrics(provider.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>()), provider.GetRequiredService<IServiceScopeFactory>(), SystemClock.Instance);
        var sync = new SnapshotSync<TerritorySnapshot, GeorefTerritorySource>(
            new GeorefTerritorySource(Options.Create(new GeorefOptions())),
            new TerritoryTarget(db, SystemClock.Instance),
            new ConditionalFetcher(new HttpClient(georef, disposeHandler: false)),
            db,
            SystemClock.Instance,
            metrics,
            logger);
        await sync.RunAsync(CancellationToken.None);
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add((logLevel, formatter(state, exception)));
    }
}
