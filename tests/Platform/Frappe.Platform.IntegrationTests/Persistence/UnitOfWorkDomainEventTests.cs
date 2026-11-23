using System.Diagnostics.Metrics;
using Frappe.Aggregates;
using Frappe.Persistence;
using Frappe.Platform.Testing.Observability;
using Frappe.UseCases;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Frappe.Platform.IntegrationTests.Persistence;

/// <summary>
/// What the unit of work does with the domain events of the aggregates it saves, against real Postgres, in a module without an
/// outbox: each event reaches its in-unit handlers once however many times the unit saves, their changes commit or roll back
/// with the event's own, one failing after-commit step never stops the next, and what the unit refuses or cannot do after a
/// commit is counted.
/// </summary>
[ClassDataSource<PostgresServer>(Shared = SharedType.PerTestSession)]
public sealed class UnitOfWorkDomainEventTests(PostgresServer server)
{
    private const string NameKey = "ux_event_widgets_name";

    private static Guid FixedId(int number) => new(number, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    private async Task<Composition> ComposeAsync(
        Action<CommitRejectionMap>? rules = null, Action<IServiceCollection>? handlers = null)
    {
        var database = "events_" + Guid.NewGuid().ToString("N")[..12];
        await using (var admin = new NpgsqlConnection(server.ConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE {database}", admin);
            await create.ExecuteNonQueryAsync();
        }

        var connection = new NpgsqlConnectionStringBuilder(server.ConnectionString) { Database = database, Pooling = false }.ConnectionString;
        var logs = new Logs();
        var calls = new Calls();
        var services = new ServiceCollection()
            .AddLogging(logging => logging.AddProvider(logs))
            .AddSingleton(calls)
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().Build())
            .AddModuleDbContext<EventsContext>(_ => connection, (_, options, limited) => options.UseNpgsql(limited))
            .AddUnitOfWork<EventsContext>("events")
            .AddScoped<IDomainEventHandler<WidgetAdded>, AuditingHandler>();
        handlers?.Invoke(services);
        if (rules is not null)
        {
            services.AddCommitRejections<EventsContext>(rules);
        }

        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<EventsContext>().Database.EnsureCreatedAsync();
        }

        return new Composition(provider, logs, calls, new PersistenceMeterCapture(provider.GetRequiredService<IMeterFactory>()));
    }

    private static async Task<(IUnitOfWork Unit, EventsContext Context)> BeginAsync(AsyncServiceScope scope)
    {
        var unit = scope.ServiceProvider.GetRequiredKeyedService<IUnitOfWork>("events");
        await unit.BeginAsync(CancellationToken.None);
        return (unit, scope.ServiceProvider.GetRequiredService<EventsContext>());
    }

    private static Widget Announced(int number, string name)
    {
        var widget = new Widget { Id = FixedId(number), Name = name };
        widget.Announce();
        return widget;
    }

    private static async Task<(int Widgets, int Audits)> CountsAsync(ServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<EventsContext>();
        return (await context.Widgets.CountAsync(), await context.Audits.CountAsync());
    }

    [Test]
    public async Task An_event_saved_and_then_committed_reaches_each_handler_once()
    {
        // Arrange
        await using var composition = await ComposeAsync(handlers: services =>
            services.AddScoped<IAfterCommitDomainEventHandler<WidgetAdded>, RecordingAfterCommit>());
        await using var scope = composition.Services.CreateAsyncScope();
        var (unit, context) = await BeginAsync(scope);
        context.Widgets.Add(Announced(1, "bolt"));

        // Act
        await unit.SaveChangesAsync(CancellationToken.None);
        await unit.CommitAsync(CancellationToken.None);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(composition.Calls.InUnit).IsEqualTo(1);
            await Assert.That(composition.Calls.AfterCommit).IsEqualTo(1);
            await Assert.That(await CountsAsync(composition.Services)).IsEqualTo((1, 1));
        }
    }

    [Test]
    public async Task An_in_unit_handler_adds_its_row_to_the_save_of_the_event_and_both_commit_together()
    {
        // Arrange
        await using var composition = await ComposeAsync();
        await using var scope = composition.Services.CreateAsyncScope();
        var (unit, context) = await BeginAsync(scope);
        context.Widgets.Add(Announced(2, "nut"));

        // Act
        await unit.CommitAsync(CancellationToken.None);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(composition.Calls.InUnit).IsEqualTo(1);
            await Assert.That(await CountsAsync(composition.Services)).IsEqualTo((1, 1));
        }
    }

    [Test]
    public async Task A_rolled_back_unit_persists_neither_the_change_nor_the_row_its_handler_added()
    {
        // Arrange
        await using var composition = await ComposeAsync();
        await using (var seeding = composition.Services.CreateAsyncScope())
        {
            var (seedUnit, seedContext) = await BeginAsync(seeding);
            seedContext.Widgets.Add(new Widget { Id = FixedId(3), Name = "bolt" });
            await seedUnit.CommitAsync(CancellationToken.None);
        }

        await using var scope = composition.Services.CreateAsyncScope();
        var (unit, context) = await BeginAsync(scope);
        context.Widgets.Add(Announced(4, "bolt"));

        // Act
        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => unit.CommitAsync(CancellationToken.None));
        await unit.RollbackAsync(CancellationToken.None);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(failure).IsNotNull();
            await Assert.That(composition.Calls.InUnit).IsEqualTo(1);
            await Assert.That(await CountsAsync(composition.Services)).IsEqualTo((1, 0));
        }
    }

    [Test]
    public async Task A_repair_that_saves_again_does_not_run_the_in_unit_handler_a_second_time()
    {
        // Arrange
        await using var composition = await ComposeAsync(map => map.On(NameKey, failure =>
        {
            foreach (var duplicate in failure.Context.ChangeTracker.Entries<Widget>()
                .Where(e => e.State == EntityState.Added && e.Entity.Name == "bolt").ToArray())
            {
                duplicate.State = EntityState.Detached;
            }

            return CommitResolution.Repair;
        }));
        await using (var seeding = composition.Services.CreateAsyncScope())
        {
            var (seedUnit, seedContext) = await BeginAsync(seeding);
            seedContext.Widgets.Add(new Widget { Id = FixedId(5), Name = "bolt" });
            await seedUnit.CommitAsync(CancellationToken.None);
        }

        await using var scope = composition.Services.CreateAsyncScope();
        var (unit, context) = await BeginAsync(scope);
        context.Widgets.AddRange(Announced(6, "washer"), Announced(7, "bolt"));

        // Act
        await unit.CommitAsync(CancellationToken.None);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(composition.Calls.InUnit).IsEqualTo(2);
            await Assert.That((await CountsAsync(composition.Services)).Widgets).IsEqualTo(2);
        }
    }

    [Test]
    public async Task A_failing_after_commit_step_does_not_stop_the_next_one_nor_the_commit_and_is_logged_and_counted()
    {
        // Arrange
        await using var composition = await ComposeAsync(handlers: services => services
            .AddScoped<IAfterCommitDomainEventHandler<WidgetAdded>, FailingAfterCommit>()
            .AddScoped<IAfterCommitDomainEventHandler<WidgetAdded>, RecordingAfterCommit>());
        await using var scope = composition.Services.CreateAsyncScope();
        var (unit, context) = await BeginAsync(scope);
        context.Widgets.Add(Announced(8, "rivet"));

        // Act
        await unit.CommitAsync(CancellationToken.None);

        // Assert
        var counted = composition.Meters.Of(PersistenceMetrics.AfterCommitFailedMetric).Single();
        using (Assert.Multiple())
        {
            await Assert.That(composition.Calls.AfterCommit).IsEqualTo(1);
            await Assert.That(await CountsAsync(composition.Services)).IsEqualTo((1, 1));
            await Assert.That(composition.Logs.Entries.Count(entry =>
                entry.Level == LogLevel.Warning && entry.Message.Contains(nameof(FailingAfterCommit), StringComparison.Ordinal))).IsEqualTo(1);
            await Assert.That(counted.Value).IsEqualTo(1);
            await Assert.That(counted.Tags[PersistenceMetrics.ModuleTag]).IsEqualTo("events");
            await Assert.That(counted.Tags[PersistenceMetrics.ReasonTag]).IsEqualTo(PersistenceMetrics.StepFailed);
        }
    }

    [Test]
    public async Task After_commit_steps_the_container_cannot_build_are_logged_as_an_error_and_counted_while_the_commit_stands()
    {
        // Arrange
        await using var composition = await ComposeAsync(handlers: services => services
            .AddScoped<IAfterCommitDomainEventHandler<WidgetAdded>>(_ => throw new InvalidOperationException("A dependency is missing.")));
        await using var scope = composition.Services.CreateAsyncScope();
        var (unit, context) = await BeginAsync(scope);
        context.Widgets.Add(Announced(9, "pin"));

        // Act
        await unit.CommitAsync(CancellationToken.None);

        // Assert
        var counted = composition.Meters.Of(PersistenceMetrics.AfterCommitFailedMetric).Single();
        using (Assert.Multiple())
        {
            await Assert.That(await CountsAsync(composition.Services)).IsEqualTo((1, 1));
            await Assert.That(composition.Logs.Entries.Count(entry => entry.Level == LogLevel.Error)).IsEqualTo(1);
            await Assert.That(counted.Tags[PersistenceMetrics.ReasonTag]).IsEqualTo(PersistenceMetrics.StepsUnresolved);
        }
    }

    [Test]
    public async Task A_commit_a_rule_rejects_is_counted_by_module()
    {
        // Arrange
        await using var composition = await ComposeAsync(map => map.On(NameKey, failure => CommitResolution.Reject(new LostRaceException())));
        await using (var seeding = composition.Services.CreateAsyncScope())
        {
            var (seedUnit, seedContext) = await BeginAsync(seeding);
            seedContext.Widgets.Add(new Widget { Id = FixedId(10), Name = "bolt" });
            await seedUnit.CommitAsync(CancellationToken.None);
        }

        await using var scope = composition.Services.CreateAsyncScope();
        var (unit, context) = await BeginAsync(scope);
        context.Widgets.Add(new Widget { Id = FixedId(11), Name = "bolt" });

        // Act
        var rejection = await Assert.ThrowsAsync<LostRaceException>(() => unit.CommitAsync(CancellationToken.None));

        // Assert
        var counted = composition.Meters.Of(PersistenceMetrics.CommitRejectedMetric).Single();
        using (Assert.Multiple())
        {
            await Assert.That(rejection).IsNotNull();
            await Assert.That(counted.Value).IsEqualTo(1);
            await Assert.That(counted.Tags[PersistenceMetrics.ModuleTag]).IsEqualTo("events");
        }
    }

    [Test]
    public async Task Repairs_that_run_out_are_counted_by_module()
    {
        // Arrange
        await using var composition = await ComposeAsync(map => map.On(NameKey, failure => CommitResolution.Repair));
        await using (var seeding = composition.Services.CreateAsyncScope())
        {
            var (seedUnit, seedContext) = await BeginAsync(seeding);
            seedContext.Widgets.Add(new Widget { Id = FixedId(12), Name = "bolt" });
            await seedUnit.CommitAsync(CancellationToken.None);
        }

        await using var scope = composition.Services.CreateAsyncScope();
        var (unit, context) = await BeginAsync(scope);
        context.Widgets.Add(new Widget { Id = FixedId(13), Name = "bolt" });

        // Act
        var conflict = await Assert.ThrowsAsync<CommitConflictException>(() => unit.CommitAsync(CancellationToken.None));

        // Assert
        var counted = composition.Meters.Of(PersistenceMetrics.RepairsExhaustedMetric).Single();
        using (Assert.Multiple())
        {
            await Assert.That(conflict).IsNotNull();
            await Assert.That(counted.Value).IsEqualTo(1);
            await Assert.That(counted.Tags[PersistenceMetrics.ModuleTag]).IsEqualTo("events");
        }
    }

    /// <summary>A widget was added.</summary>
    private sealed record WidgetAdded(Guid Id) : IDomainEvent;

    /// <summary>Counts the calls the handlers of a test received.</summary>
    private sealed class Calls
    {
        public int InUnit { get; set; }

        public int AfterCommit { get; set; }
    }

    /// <summary>Adds an audit row for the widget, in the save of the event.</summary>
    private sealed class AuditingHandler(EventsContext context, Calls calls) : IDomainEventHandler<WidgetAdded>
    {
        public Task HandleAsync(WidgetAdded domainEvent, CancellationToken cancellationToken)
        {
            calls.InUnit++;
            context.Audits.Add(new Audit { Id = Guid.NewGuid(), WidgetId = domainEvent.Id });
            return Task.CompletedTask;
        }
    }

    /// <summary>Notes that it ran after the commit.</summary>
    private sealed class RecordingAfterCommit(Calls calls) : IAfterCommitDomainEventHandler<WidgetAdded>
    {
        public Task HandleAsync(WidgetAdded domainEvent, CancellationToken cancellationToken)
        {
            calls.AfterCommit++;
            return Task.CompletedTask;
        }
    }

    /// <summary>Always fails.</summary>
    private sealed class FailingAfterCommit : IAfterCommitDomainEventHandler<WidgetAdded>
    {
        public Task HandleAsync(WidgetAdded domainEvent, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The cache is down.");
    }

    /// <summary>A row with a unique name, which raises an event when announced.</summary>
    private sealed class Widget : AggregateRoot
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public void Announce() => Raise(new WidgetAdded(Id));
    }

    /// <summary>What the in-unit handler writes beside the widget.</summary>
    private sealed class Audit
    {
        public Guid Id { get; set; }

        public Guid WidgetId { get; set; }
    }

    /// <summary>The database context of the widgets and their audit rows.</summary>
    private sealed class EventsContext(DbContextOptions<EventsContext> options) : DbContext(options)
    {
        public DbSet<Widget> Widgets => Set<Widget>();

        public DbSet<Audit> Audits => Set<Audit>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Widget>(widget =>
            {
                widget.ToTable("widgets");
                widget.Ignore(w => w.DomainEvents);
                widget.HasIndex(w => w.Name).IsUnique().HasDatabaseName(NameKey);
            });
            modelBuilder.Entity<Audit>().ToTable("audits");
        }
    }

    /// <summary>The services of one test with what it recorded.</summary>
    private sealed class Composition(ServiceProvider services, Logs logs, Calls calls, PersistenceMeterCapture meters) : IAsyncDisposable
    {
        public ServiceProvider Services => services;

        public Logs Logs => logs;

        public Calls Calls => calls;

        public PersistenceMeterCapture Meters => meters;

        public async ValueTask DisposeAsync()
        {
            meters.Dispose();
            await services.DisposeAsync();
        }
    }

    /// <summary>A logger provider that keeps every entry.</summary>
    private sealed class Logs : ILoggerProvider
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public ILogger CreateLogger(string categoryName) => new Capture(Entries);

        public void Dispose()
        {
        }

        /// <summary>A logger that adds its entries to the list of its provider.</summary>
        private sealed class Capture(List<(LogLevel Level, string Message)> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (entries)
                {
                    entries.Add((logLevel, formatter(state, exception)));
                }
            }
        }
    }
}
