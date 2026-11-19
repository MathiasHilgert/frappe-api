using Frappe.Aggregates;
using Frappe.Persistence;
using Frappe.UseCases;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Frappe.Platform.IntegrationTests.Persistence;

/// <summary>
/// The unit of work of a module against real Postgres: it commits what a command staged in one transaction, hands the
/// domain events to the in-unit handlers before the save and to the after-commit handlers once the data is committed,
/// and nests in a transaction somebody else owns without committing it.
/// </summary>
[ClassDataSource<PostgresServer>(Shared = SharedType.PerTestSession)]
public sealed class UnitOfWorkTests(PostgresServer server)
{
    private const string Module = "orders";

    private async Task<ServiceProvider> ComposeAsync(Probe probe, Action<IServiceCollection>? configure = null)
    {
        var database = "uow_" + Guid.NewGuid().ToString("N")[..12];
        await using (var admin = new NpgsqlConnection(server.ConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE {database}", admin);
            await create.ExecuteNonQueryAsync();
        }

        var connection = new NpgsqlConnectionStringBuilder(server.ConnectionString) { Database = database, Pooling = false }.ConnectionString;
        var services = new ServiceCollection()
            .AddLogging(logging => logging.AddProvider(probe))
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().Build())
            .AddSingleton(probe)
            .AddModuleDbContext<OrdersContext>(_ => connection, (_, options, limited) => options.UseNpgsql(limited))
            .AddUnitOfWork<OrdersContext>(Module);
        configure?.Invoke(services);

        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<OrdersContext>().Database.EnsureCreatedAsync();
        return provider;
    }

    private static async Task<IUnitOfWork> BeginAsync(AsyncServiceScope scope)
    {
        var unitOfWork = scope.ServiceProvider.GetRequiredKeyedService<IUnitOfWork>(Module);
        await unitOfWork.BeginAsync(CancellationToken.None);
        return unitOfWork;
    }

    private static OrdersContext Context(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<OrdersContext>();

    private static async Task<int[]> StoredAsync(ServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        return await Context(scope).Orders.OrderBy(order => order.Number).Select(order => order.Number).ToArrayAsync();
    }

    [Test]
    public async Task Committing_writes_everything_the_command_staged()
    {
        // Arrange
        await using var services = await ComposeAsync(new Probe());
        await using var scope = services.CreateAsyncScope();
        var unitOfWork = await BeginAsync(scope);
        Context(scope).Orders.AddRange(Order.Place(1), Order.Place(2));

        // Act
        await unitOfWork.CommitAsync(CancellationToken.None);

        // Assert
        await Assert.That(await StoredAsync(services)).IsEquivalentTo([1, 2]);
    }

    [Test]
    public async Task Rolling_back_discards_what_was_staged_and_a_later_commit_writes_none_of_it()
    {
        // Arrange
        await using var services = await ComposeAsync(new Probe());
        await using var scope = services.CreateAsyncScope();
        var unitOfWork = await BeginAsync(scope);
        Context(scope).Orders.Add(Order.Place(1));

        // Act
        await unitOfWork.RollbackAsync(CancellationToken.None);
        await unitOfWork.CommitAsync(CancellationToken.None);

        // Assert
        await Assert.That(await StoredAsync(services)).IsEmpty();
    }

    [Test]
    public async Task An_in_unit_handler_writes_its_changes_in_the_same_save_as_the_aggregate()
    {
        // Arrange
        await using var services = await ComposeAsync(new Probe(), s => s.AddScoped<IDomainEventHandler<OrderPlaced>, ProjectionHandler>());
        await using var scope = services.CreateAsyncScope();
        var unitOfWork = await BeginAsync(scope);
        Context(scope).Orders.Add(Order.Place(1));

        // Act
        await unitOfWork.CommitAsync(CancellationToken.None);

        // Assert
        await using var reader = services.CreateAsyncScope();
        var projected = await Context(reader).Projections.Select(projection => projection.Number).ToArrayAsync();
        await Assert.That(projected).IsEquivalentTo([1]);
    }

    [Test]
    public async Task A_failing_in_unit_handler_fails_the_commit_and_nothing_is_written()
    {
        // Arrange
        await using var services = await ComposeAsync(new Probe(), s => s.AddScoped<IDomainEventHandler<OrderPlaced>, FailingHandler>());
        await using var scope = services.CreateAsyncScope();
        var unitOfWork = await BeginAsync(scope);
        Context(scope).Orders.Add(Order.Place(1));

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => unitOfWork.CommitAsync(CancellationToken.None));
        await unitOfWork.RollbackAsync(CancellationToken.None);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(failure!.Message).IsEqualTo("handler refused");
            await Assert.That(await StoredAsync(services)).IsEmpty();
        }
    }

    [Test]
    public async Task After_commit_handlers_run_once_the_data_is_committed()
    {
        // Arrange
        var probe = new Probe();
        await using var services = await ComposeAsync(probe, s => s.AddScoped<IAfterCommitDomainEventHandler<OrderPlaced>, SeenAfterCommit>());
        await using var scope = services.CreateAsyncScope();
        var unitOfWork = await BeginAsync(scope);
        Context(scope).Orders.Add(Order.Place(7));

        // Act
        await unitOfWork.CommitAsync(CancellationToken.None);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(probe.AfterCommit).IsEquivalentTo([7]);
            await Assert.That(probe.StoredWhenAfterCommitRan).IsEquivalentTo([7]);
        }
    }

    [Test]
    public async Task After_commit_handlers_ignore_the_cancellation_of_the_command_token()
    {
        // Arrange
        var probe = new Probe();
        await using var services = await ComposeAsync(probe, s => s.AddScoped<IAfterCommitDomainEventHandler<OrderPlaced>, SeenAfterCommit>());
        await using var scope = services.CreateAsyncScope();
        using var command = new CancellationTokenSource();
        var context = Context(scope);
        context.SavedChanges += (_, _) => command.Cancel();
        var unitOfWork = scope.ServiceProvider.GetRequiredKeyedService<IUnitOfWork>(Module);
        context.Orders.Add(Order.Place(8));

        // Act
        await unitOfWork.SaveChangesAsync(command.Token);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(command.IsCancellationRequested).IsTrue();
            await Assert.That(await StoredAsync(services)).IsEquivalentTo([8]);
            await Assert.That(probe.AfterCommit).IsEquivalentTo([8]);
            await Assert.That(probe.AfterCommitTokenCancelled).IsEquivalentTo([false]);
        }
    }

    [Test]
    public async Task After_commit_handlers_do_not_run_when_the_commit_fails()
    {
        // Arrange
        var probe = new Probe();
        await using var services = await ComposeAsync(probe, s => s
            .AddScoped<IDomainEventHandler<OrderPlaced>, FailingHandler>()
            .AddScoped<IAfterCommitDomainEventHandler<OrderPlaced>, SeenAfterCommit>());
        await using var scope = services.CreateAsyncScope();
        var unitOfWork = await BeginAsync(scope);
        Context(scope).Orders.Add(Order.Place(7));

        // Act
        await Assert.ThrowsAsync<InvalidOperationException>(() => unitOfWork.CommitAsync(CancellationToken.None));
        await unitOfWork.RollbackAsync(CancellationToken.None);

        // Assert
        await Assert.That(probe.AfterCommit).IsEmpty();
    }

    [Test]
    public async Task A_failing_after_commit_handler_is_logged_and_neither_stops_the_next_one_nor_undoes_the_commit()
    {
        // Arrange
        var probe = new Probe();
        await using var services = await ComposeAsync(probe, s => s
            .AddScoped<IAfterCommitDomainEventHandler<OrderPlaced>, FailingAfterCommit>()
            .AddScoped<IAfterCommitDomainEventHandler<OrderPlaced>, SeenAfterCommit>());
        await using var scope = services.CreateAsyncScope();
        var unitOfWork = await BeginAsync(scope);
        Context(scope).Orders.Add(Order.Place(7));

        // Act
        await unitOfWork.CommitAsync(CancellationToken.None);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(probe.AfterCommit).IsEquivalentTo([7]);
            await Assert.That(probe.Warnings).Contains(message => message.Contains(nameof(OrderPlaced)) && message.Contains(nameof(FailingAfterCommit)));
            await Assert.That(await StoredAsync(services)).IsEquivalentTo([7]);
        }
    }

    [Test]
    public async Task After_commit_handlers_that_the_container_cannot_build_are_logged_and_the_commit_stands()
    {
        // Arrange
        var probe = new Probe();
        await using var services = await ComposeAsync(probe, s => s.AddScoped<IAfterCommitDomainEventHandler<OrderPlaced>>(_ => throw new InvalidOperationException("not buildable")));
        await using var scope = services.CreateAsyncScope();
        var unitOfWork = await BeginAsync(scope);
        Context(scope).Orders.Add(Order.Place(7));

        // Act
        await unitOfWork.CommitAsync(CancellationToken.None);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(probe.Errors).Contains(message => message.Contains(nameof(OrderPlaced)));
            await Assert.That(await StoredAsync(services)).IsEquivalentTo([7]);
        }
    }

    [Test]
    public async Task Saving_outside_a_unit_is_its_own_commit_and_runs_the_after_commit_handlers()
    {
        // Arrange
        var probe = new Probe();
        await using var services = await ComposeAsync(probe, s => s.AddScoped<IAfterCommitDomainEventHandler<OrderPlaced>, SeenAfterCommit>());
        await using var scope = services.CreateAsyncScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredKeyedService<IUnitOfWork>(Module);
        Context(scope).Orders.Add(Order.Place(3));

        // Act
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(probe.AfterCommit).IsEquivalentTo([3]);
            await Assert.That(await StoredAsync(services)).IsEquivalentTo([3]);
        }
    }

    [Test]
    public async Task Inside_a_transaction_of_somebody_else_committing_keeps_the_changes_for_its_owner_and_runs_no_after_commit_handler()
    {
        // Arrange
        var probe = new Probe();
        await using var services = await ComposeAsync(probe, s => s.AddScoped<IAfterCommitDomainEventHandler<OrderPlaced>, SeenAfterCommit>());
        await using var scope = services.CreateAsyncScope();
        var context = Context(scope);
        await using var owner = await context.Database.BeginTransactionAsync();
        context.Orders.Add(Order.Place(1));
        var unitOfWork = await BeginAsync(scope);
        context.Orders.Add(Order.Place(2));

        // Act
        await unitOfWork.CommitAsync(CancellationToken.None);
        var visibleToOthersBeforeOwnerCommits = await StoredAsync(services);
        await owner.CommitAsync();

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(visibleToOthersBeforeOwnerCommits).IsEmpty();
            await Assert.That(await StoredAsync(services)).IsEquivalentTo([1, 2]);
            await Assert.That(probe.AfterCommit).IsEmpty();
        }
    }

    [Test]
    public async Task Inside_a_transaction_of_somebody_else_rolling_back_keeps_what_the_owner_staged_before()
    {
        // Arrange
        await using var services = await ComposeAsync(new Probe());
        await using var scope = services.CreateAsyncScope();
        var context = Context(scope);
        await using var owner = await context.Database.BeginTransactionAsync();
        context.Orders.Add(Order.Place(1));
        var unitOfWork = await BeginAsync(scope);
        context.Orders.Add(Order.Place(2));
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        // Act
        await unitOfWork.RollbackAsync(CancellationToken.None);
        await owner.CommitAsync();

        // Assert
        await Assert.That(await StoredAsync(services)).IsEquivalentTo([1]);
    }

    [Test]
    public async Task Saving_and_then_committing_handles_each_event_once()
    {
        // Arrange
        var probe = new Probe();
        await using var services = await ComposeAsync(probe, s => s
            .AddScoped<IDomainEventHandler<OrderPlaced>, SeenInUnit>()
            .AddScoped<IAfterCommitDomainEventHandler<OrderPlaced>, SeenAfterCommit>());
        await using var scope = services.CreateAsyncScope();
        var unitOfWork = await BeginAsync(scope);
        Context(scope).Orders.Add(Order.Place(5));

        // Act
        await unitOfWork.SaveChangesAsync(CancellationToken.None);
        await unitOfWork.CommitAsync(CancellationToken.None);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(probe.InUnit).IsEquivalentTo([5]);
            await Assert.That(probe.AfterCommit).IsEquivalentTo([5]);
        }
    }

    [Test]
    public async Task Every_in_unit_handler_sees_every_event_of_the_commit()
    {
        // Arrange
        var probe = new Probe();
        await using var services = await ComposeAsync(probe, s => s
            .AddScoped<IDomainEventHandler<OrderPlaced>, SeenInUnit>()
            .AddScoped<IDomainEventHandler<OrderPlaced>, SeenInUnitToo>());
        await using var scope = services.CreateAsyncScope();
        var unitOfWork = await BeginAsync(scope);
        Context(scope).Orders.AddRange(Order.Place(1), Order.Place(2));

        // Act
        await unitOfWork.CommitAsync(CancellationToken.None);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(probe.InUnit).IsEquivalentTo([1, 2]);
            await Assert.That(probe.InUnitOther).IsEquivalentTo([1, 2]);
        }
    }

    [Test]
    public async Task Saving_inside_a_transaction_of_somebody_else_runs_no_after_commit_handler()
    {
        // Arrange
        var probe = new Probe();
        await using var services = await ComposeAsync(probe, s => s.AddScoped<IAfterCommitDomainEventHandler<OrderPlaced>, SeenAfterCommit>());
        await using var scope = services.CreateAsyncScope();
        var context = Context(scope);
        await using var owner = await context.Database.BeginTransactionAsync();
        var unitOfWork = scope.ServiceProvider.GetRequiredKeyedService<IUnitOfWork>(Module);
        context.Orders.Add(Order.Place(4));

        // Act
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        // Assert
        await Assert.That(probe.AfterCommit).IsEmpty();
    }

    [Test]
    public async Task Inside_a_transaction_of_somebody_else_an_in_unit_handler_writes_its_projection_for_the_owner()
    {
        // Arrange
        await using var services = await ComposeAsync(new Probe(), s => s.AddScoped<IDomainEventHandler<OrderPlaced>, ProjectionHandler>());
        await using var scope = services.CreateAsyncScope();
        var context = Context(scope);
        await using var owner = await context.Database.BeginTransactionAsync();
        context.Orders.Add(Order.Place(1));
        var unitOfWork = await BeginAsync(scope);
        context.Orders.Add(Order.Place(2));

        // Act
        await unitOfWork.CommitAsync(CancellationToken.None);
        await owner.CommitAsync();

        // Assert
        await using var reader = services.CreateAsyncScope();
        var projected = await Context(reader).Projections.Select(projection => projection.Number).ToArrayAsync();
        await Assert.That(projected).IsEquivalentTo([1, 2]);
    }

    [Test]
    public async Task Inside_a_transaction_of_somebody_else_a_failing_in_unit_handler_rolls_back_to_the_savepoint_and_keeps_the_owner_row()
    {
        // Arrange
        await using var services = await ComposeAsync(new Probe(), s => s.AddScoped<IDomainEventHandler<OrderPlaced>, FailingOnTwoHandler>());
        await using var scope = services.CreateAsyncScope();
        var context = Context(scope);
        await using var owner = await context.Database.BeginTransactionAsync();
        context.Orders.Add(Order.Place(1));
        var unitOfWork = await BeginAsync(scope);
        context.Orders.Add(Order.Place(2));

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => unitOfWork.CommitAsync(CancellationToken.None));
        await unitOfWork.RollbackAsync(CancellationToken.None);
        await owner.CommitAsync();

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(failure!.Message).IsEqualTo("handler refused");
            await Assert.That(await StoredAsync(services)).IsEquivalentTo([1]);
        }
    }

    /// <summary>Records what the handlers saw and what the unit logged.</summary>
    private sealed class Probe : ILoggerProvider, ILogger
    {
        public List<int> InUnit { get; } = [];

        public List<int> InUnitOther { get; } = [];

        public List<int> AfterCommit { get; } = [];

        public List<int> StoredWhenAfterCommitRan { get; } = [];

        public List<bool> AfterCommitTokenCancelled { get; } = [];

        public List<string> Warnings { get; } = [];

        public List<string> Errors { get; } = [];

        public ILogger CreateLogger(string categoryName) => this;

        public void Dispose()
        {
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            if (logLevel == LogLevel.Warning)
            {
                Warnings.Add(message);
            }
            else if (logLevel == LogLevel.Error)
            {
                Errors.Add(message);
            }
        }
    }

    /// <summary>The event an order raises when it is placed.</summary>
    private sealed record OrderPlaced(int Number) : IDomainEvent;

    /// <summary>An aggregate that raises an event when placed.</summary>
    private sealed class Order : AggregateRoot
    {
        public Guid Id { get; private init; } = Guid.NewGuid();

        public int Number { get; private init; }

        public static Order Place(int number)
        {
            var order = new Order { Number = number };
            order.Raise(new OrderPlaced(number));
            return order;
        }
    }

    /// <summary>A read model row an in-unit handler writes.</summary>
    private sealed class Projection
    {
        public Guid Id { get; init; } = Guid.NewGuid();

        public int Number { get; init; }
    }

    /// <summary>Stages a projection row for the placed order.</summary>
    private sealed class ProjectionHandler(OrdersContext db) : IDomainEventHandler<OrderPlaced>
    {
        public Task HandleAsync(OrderPlaced domainEvent, CancellationToken cancellationToken)
        {
            db.Projections.Add(new Projection { Number = domainEvent.Number });
            return Task.CompletedTask;
        }
    }

    /// <summary>Records the events it saw inside the unit.</summary>
    private sealed class SeenInUnit(Probe probe) : IDomainEventHandler<OrderPlaced>
    {
        public Task HandleAsync(OrderPlaced domainEvent, CancellationToken cancellationToken)
        {
            probe.InUnit.Add(domainEvent.Number);
            return Task.CompletedTask;
        }
    }

    /// <summary>Records the events it saw inside the unit, apart from the other recorder.</summary>
    private sealed class SeenInUnitToo(Probe probe) : IDomainEventHandler<OrderPlaced>
    {
        public Task HandleAsync(OrderPlaced domainEvent, CancellationToken cancellationToken)
        {
            probe.InUnitOther.Add(domainEvent.Number);
            return Task.CompletedTask;
        }
    }

    /// <summary>Refuses the event inside the unit.</summary>
    private sealed class FailingHandler : IDomainEventHandler<OrderPlaced>
    {
        public Task HandleAsync(OrderPlaced domainEvent, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("handler refused");
    }

    /// <summary>Refuses the order numbered two only.</summary>
    private sealed class FailingOnTwoHandler : IDomainEventHandler<OrderPlaced>
    {
        public Task HandleAsync(OrderPlaced domainEvent, CancellationToken cancellationToken) =>
            domainEvent.Number == 2 ? throw new InvalidOperationException("handler refused") : Task.CompletedTask;
    }

    /// <summary>Records the event and the committed orders it can read.</summary>
    private sealed class SeenAfterCommit(Probe probe, OrdersContext db) : IAfterCommitDomainEventHandler<OrderPlaced>
    {
        public async Task HandleAsync(OrderPlaced domainEvent, CancellationToken cancellationToken)
        {
            probe.AfterCommit.Add(domainEvent.Number);
            probe.AfterCommitTokenCancelled.Add(cancellationToken.IsCancellationRequested);
            probe.StoredWhenAfterCommitRan.AddRange(await db.Orders.Select(order => order.Number).ToArrayAsync(cancellationToken));
        }
    }

    /// <summary>Fails after the commit.</summary>
    private sealed class FailingAfterCommit : IAfterCommitDomainEventHandler<OrderPlaced>
    {
        public Task HandleAsync(OrderPlaced domainEvent, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("cache is down");
    }

    /// <summary>The module context under test.</summary>
    private sealed class OrdersContext(DbContextOptions<OrdersContext> options) : DbContext(options)
    {
        public DbSet<Order> Orders => Set<Order>();

        public DbSet<Projection> Projections => Set<Projection>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Order>().Ignore(order => order.DomainEvents);
        }
    }
}
