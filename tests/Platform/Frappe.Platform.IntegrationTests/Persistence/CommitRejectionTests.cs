using System.Data.Common;
using Frappe.Aggregates;
using Frappe.Persistence;
using Frappe.Results;
using Frappe.UseCases;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Frappe.Platform.IntegrationTests.Persistence;

/// <summary>
/// What the unit of work does when the storage refuses a commit (UOW-05), against real Postgres: a constraint name, a
/// lost concurrent change or any other failure is mapped by the rules the module registered, a rule may repair the
/// staged changes so the save runs again, and a failure no rule claims passes through unchanged.
/// </summary>
[ClassDataSource<PostgresServer>(Shared = SharedType.PerTestSession)]
public sealed class CommitRejectionTests(PostgresServer server)
{
    /// <summary>A readable fixed id: the same number always gives the same <see cref="Guid"/>.</summary>
    private static Guid FixedId(int number) => new(number, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    private const string NameKey = "ux_widgets_name";

    private const string CodeKey = "xc_widgets_code";

    private async Task<ServiceProvider> ComposeAsync(Action<CommitRejectionMap>? rules, Recorder? recorder = null)
    {
        var database = "rejections_" + Guid.NewGuid().ToString("N")[..12];
        await using (var admin = new NpgsqlConnection(server.ConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE {database}", admin);
            await create.ExecuteNonQueryAsync();
        }

        var connection = new NpgsqlConnectionStringBuilder(server.ConnectionString) { Database = database, Pooling = false }.ConnectionString;
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().Build())
            .AddModuleDbContext<WidgetsContext>(_ => connection, (_, options, limited) =>
            {
                options.UseNpgsql(limited);
                if (recorder is not null)
                {
                    options.AddInterceptors(recorder);
                }
            })
            .AddUnitOfWork<WidgetsContext>("widgets");
        if (recorder is not null)
        {
            services.AddSingleton<IAfterCommitDomainEventHandler<WidgetAdded>>(recorder);
        }

        if (rules is not null)
        {
            services.AddCommitRejections<WidgetsContext>(rules);
        }

        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<WidgetsContext>();
        await context.Database.EnsureCreatedAsync();
        await context.Database.ExecuteSqlRawAsync($"ALTER TABLE widgets ADD CONSTRAINT {CodeKey} UNIQUE (code) DEFERRABLE INITIALLY DEFERRED");
        return provider;
    }

    private static async Task<IUnitOfWork> BeginAsync(AsyncServiceScope scope)
    {
        var unitOfWork = scope.ServiceProvider.GetRequiredKeyedService<IUnitOfWork>("widgets");
        await unitOfWork.BeginAsync(CancellationToken.None);
        return unitOfWork;
    }

    private static async Task SeedAsync(ServiceProvider services, params string[] names)
    {
        await using var scope = services.CreateAsyncScope();
        var unitOfWork = await BeginAsync(scope);
        scope.ServiceProvider.GetRequiredService<WidgetsContext>().Widgets.AddRange(names.Select(n => new Widget { Id = FixedId(1), Name = n }));
        await unitOfWork.CommitAsync(CancellationToken.None);
    }

    private static async Task<int> CountAsync(ServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<WidgetsContext>().Widgets.CountAsync();
    }

    [Test]
    public async Task A_unique_violation_of_a_mapped_constraint_is_the_modules_rejection_and_writes_nothing()
    {
        // Arrange
        await using var services = await ComposeAsync(map => map.On(NameKey, failure => CommitResolution.Reject(new LostRaceException("name taken"))));
        await SeedAsync(services, "bolt");

        await using var scope = services.CreateAsyncScope();
        var unitOfWork = await BeginAsync(scope);
        var context = scope.ServiceProvider.GetRequiredService<WidgetsContext>();
        context.Widgets.AddRange(new Widget { Id = FixedId(2), Name = "nut" }, new Widget { Id = FixedId(3), Name = "bolt" });

        // Act
        var rejection = await Assert.ThrowsAsync<LostRaceException>(() => unitOfWork.CommitAsync(CancellationToken.None));
        await unitOfWork.RollbackAsync(CancellationToken.None);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(rejection!.Message).IsEqualTo("name taken");
            await Assert.That(await CountAsync(services)).IsEqualTo(1);
        }
    }

    private static async Task SeedCodedAsync(ServiceProvider services, string code)
    {
        await using var scope = services.CreateAsyncScope();
        var unitOfWork = await BeginAsync(scope);
        scope.ServiceProvider.GetRequiredService<WidgetsContext>().Widgets.Add(new Widget { Id = FixedId(100), Name = "stored " + code, Code = code });
        await unitOfWork.CommitAsync(CancellationToken.None);
    }

    [Test]
    public async Task A_deferred_violation_of_a_mapped_constraint_is_the_modules_rejection_writes_nothing_and_runs_no_after_commit_handler()
    {
        // Arrange
        var recorder = new Recorder();
        await using var services = await ComposeAsync(map => map.OnDeferred(CodeKey, failure => new LostRaceException("code taken")), recorder);
        await SeedCodedAsync(services, "A");
        recorder.AfterCommit.Clear();

        await using var scope = services.CreateAsyncScope();
        var unitOfWork = await BeginAsync(scope);
        var context = scope.ServiceProvider.GetRequiredService<WidgetsContext>();
        var duplicate = new Widget { Id = FixedId(101), Name = "dup", Code = "A" };
        duplicate.Announce();
        context.Widgets.Add(duplicate);

        // Act
        var rejection = await Assert.ThrowsAsync<LostRaceException>(() => unitOfWork.CommitAsync(CancellationToken.None));
        await unitOfWork.RollbackAsync(CancellationToken.None);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(rejection!.Message).IsEqualTo("code taken");
            await Assert.That(await CountAsync(services)).IsEqualTo(1);
            await Assert.That(recorder.AfterCommit).IsEmpty();
        }
    }

    [Test]
    public async Task A_deferred_rule_reads_the_storage_at_commit_and_sees_the_stored_row()
    {
        // Arrange
        string? stored = null;
        await using var services = await ComposeAsync(map => map.OnDeferred(CodeKey, failure =>
        {
            stored = failure.Context.Set<Widget>().AsNoTracking().Where(w => w.Code == "A" && w.Id == FixedId(100)).Select(w => w.Name).Single();
            return new LostRaceException("code taken by " + stored);
        }));
        await SeedCodedAsync(services, "A");

        await using var scope = services.CreateAsyncScope();
        var unitOfWork = await BeginAsync(scope);
        scope.ServiceProvider.GetRequiredService<WidgetsContext>().Widgets.Add(new Widget { Id = FixedId(102), Name = "dup", Code = "A" });

        // Act
        var rejection = await Assert.ThrowsAsync<LostRaceException>(() => unitOfWork.CommitAsync(CancellationToken.None));
        await unitOfWork.RollbackAsync(CancellationToken.None);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(stored).IsEqualTo("stored A");
            await Assert.That(rejection!.Message).IsEqualTo("code taken by stored A");
        }
    }

    [Test]
    public async Task A_deferred_violation_no_deferred_rule_claims_passes_through_unchanged()
    {
        // Arrange
        await using var services = await ComposeAsync(map => map.OnDeferred("xc_other", failure => new LostRaceException("other")));
        await SeedCodedAsync(services, "A");

        await using var scope = services.CreateAsyncScope();
        var unitOfWork = await BeginAsync(scope);
        scope.ServiceProvider.GetRequiredService<WidgetsContext>().Widgets.Add(new Widget { Id = FixedId(103), Name = "dup", Code = "A" });

        // Act
        var failure = await Assert.ThrowsAsync<PostgresException>(() => unitOfWork.CommitAsync(CancellationToken.None));
        await unitOfWork.RollbackAsync(CancellationToken.None);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(failure!.ConstraintName).IsEqualTo(CodeKey);
            await Assert.That(await CountAsync(services)).IsEqualTo(1);
        }
    }

    [Test]
    public async Task A_module_without_deferred_rules_issues_no_set_constraints()
    {
        // Arrange
        var recorder = new Recorder();
        await using var services = await ComposeAsync(map => map.On(NameKey, failure => CommitResolution.Reject(new LostRaceException("name taken"))), recorder);
        await using var scope = services.CreateAsyncScope();
        var unitOfWork = await BeginAsync(scope);
        scope.ServiceProvider.GetRequiredService<WidgetsContext>().Widgets.Add(new Widget { Id = FixedId(104), Name = "plain" });

        // Act
        await unitOfWork.CommitAsync(CancellationToken.None);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(recorder.Commands.Any(c => c.Contains("SET CONSTRAINTS", StringComparison.OrdinalIgnoreCase))).IsFalse();
            await Assert.That(await CountAsync(services)).IsEqualTo(1);
        }
    }

    [Test]
    public async Task A_module_with_deferred_rules_checks_them_once_before_it_commits()
    {
        // Arrange
        var recorder = new Recorder();
        await using var services = await ComposeAsync(map => map.OnDeferred(CodeKey, failure => new LostRaceException("code taken")), recorder);
        await using var scope = services.CreateAsyncScope();
        var unitOfWork = await BeginAsync(scope);
        var widget = new Widget { Id = FixedId(105), Name = "plain", Code = "Z" };
        widget.Announce();
        scope.ServiceProvider.GetRequiredService<WidgetsContext>().Widgets.Add(widget);

        // Act
        await unitOfWork.CommitAsync(CancellationToken.None);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(recorder.Commands.Count(c => c.Contains("SET CONSTRAINTS ALL IMMEDIATE", StringComparison.OrdinalIgnoreCase))).IsEqualTo(1);
            await Assert.That(recorder.AfterCommit).IsEquivalentTo([FixedId(105)]);
            await Assert.That(await CountAsync(services)).IsEqualTo(1);
        }
    }

    [Test]
    public async Task A_unit_nested_in_somebody_elses_transaction_gets_the_deferred_rejection_and_leaves_the_owner_usable()
    {
        // Arrange
        var recorder = new Recorder();
        await using var services = await ComposeAsync(map => map.OnDeferred(CodeKey, failure => new LostRaceException("code taken")), recorder);
        await SeedCodedAsync(services, "A");
        recorder.AfterCommit.Clear();

        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<WidgetsContext>();
        await using var owner = await context.Database.BeginTransactionAsync();
        var unitOfWork = await BeginAsync(scope);
        var duplicate = new Widget { Id = FixedId(106), Name = "dup", Code = "A" };
        duplicate.Announce();
        context.Widgets.Add(duplicate);

        // Act
        var rejection = await Assert.ThrowsAsync<LostRaceException>(() => unitOfWork.CommitAsync(CancellationToken.None));
        await unitOfWork.RollbackAsync(CancellationToken.None);
        var ownerStillWorks = await context.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM widgets").SingleAsync();
        await owner.CommitAsync();

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(rejection!.Message).IsEqualTo("code taken");
            await Assert.That(ownerStillWorks).IsEqualTo(1);
            await Assert.That(await CountAsync(services)).IsEqualTo(1);
            await Assert.That(recorder.AfterCommit).IsEmpty();
        }
    }

    [Test]
    public async Task A_second_unit_in_the_same_owner_transaction_still_defers_its_constraints_after_the_first_checked_them()
    {
        // Arrange
        await using var services = await ComposeAsync(map => map.OnDeferred(CodeKey, failure => new LostRaceException("code taken")));
        await SeedCodedAsync(services, "A");
        await using (var seeding = services.CreateAsyncScope())
        {
            var seedUnit = await BeginAsync(seeding);
            seeding.ServiceProvider.GetRequiredService<WidgetsContext>().Widgets.Add(new Widget { Id = FixedId(107), Name = "stored B", Code = "B" });
            await seedUnit.CommitAsync(CancellationToken.None);
        }

        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<WidgetsContext>();
        await using var owner = await context.Database.BeginTransactionAsync();
        var first = await BeginAsync(scope);
        context.Widgets.Add(new Widget { Id = FixedId(108), Name = "first", Code = "Z" });
        await first.CommitAsync(CancellationToken.None);

        var second = await BeginAsync(scope);
        var one = await context.Widgets.SingleAsync(w => w.Id == FixedId(100));
        var other = await context.Widgets.SingleAsync(w => w.Id == FixedId(107));

        // Act
        (one.Code, other.Code) = ("B", "A");
        await second.CommitAsync(CancellationToken.None);
        await owner.CommitAsync();

        // Assert
        context.ChangeTracker.Clear();
        var codes = await context.Widgets.AsNoTracking().OrderBy(w => w.Id).Select(w => w.Code!).ToListAsync();
        await Assert.That(codes).IsEquivalentTo(["B", "A", "Z"]);
    }

    [Test]
    public async Task A_unit_nested_in_somebody_elses_transaction_commits_through_the_owner_when_its_deferred_check_passes()
    {
        // Arrange
        await using var services = await ComposeAsync(map => map.OnDeferred(CodeKey, failure => new LostRaceException("code taken")));
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<WidgetsContext>();
        await using var owner = await context.Database.BeginTransactionAsync();
        var unitOfWork = await BeginAsync(scope);
        context.Widgets.Add(new Widget { Id = FixedId(109), Name = "nested", Code = "N" });

        // Act
        await unitOfWork.CommitAsync(CancellationToken.None);
        await owner.CommitAsync();

        // Assert
        await Assert.That(await CountAsync(services)).IsEqualTo(1);
    }

    [Test]
    public async Task A_deferred_violation_no_rule_claims_in_a_nested_unit_rethrows_and_leaves_the_owner_usable()
    {
        // Arrange
        await using var services = await ComposeAsync(map => map.OnDeferred("xc_other", failure => new LostRaceException("other")));
        await SeedCodedAsync(services, "A");
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<WidgetsContext>();
        await using var owner = await context.Database.BeginTransactionAsync();
        var unitOfWork = await BeginAsync(scope);
        context.Widgets.Add(new Widget { Id = FixedId(110), Name = "dup", Code = "A" });

        // Act
        var failure = await Assert.ThrowsAsync<PostgresException>(() => unitOfWork.CommitAsync(CancellationToken.None));
        await unitOfWork.RollbackAsync(CancellationToken.None);
        var ownerStillWorks = await context.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM widgets").SingleAsync();
        await owner.CommitAsync();

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(failure!.ConstraintName).IsEqualTo(CodeKey);
            await Assert.That(ownerStillWorks).IsEqualTo(1);
            await Assert.That(await CountAsync(services)).IsEqualTo(1);
        }
    }

    [Test]
    public async Task A_deferred_rule_needs_a_constraint_name()
    {
        // Arrange
        var map = new CommitRejectionMap();

        // Act
        var empty = Assert.Throws<ArgumentException>(() => map.OnDeferred(" ", failure => new LostRaceException("lost")));

        // Assert
        await Assert.That(empty).IsNotNull();
    }

    [Test]
    public async Task A_lost_concurrent_change_is_the_rejection_of_the_concurrency_rule()
    {
        // Arrange
        await using var services = await ComposeAsync(map => map.OnConcurrency(failure => CommitResolution.Reject(new LostRaceException("changed"))));
        await SeedAsync(services, "bolt");

        await using var scope = services.CreateAsyncScope();
        var unitOfWork = await BeginAsync(scope);
        var widget = await scope.ServiceProvider.GetRequiredService<WidgetsContext>().Widgets.SingleAsync();
        await using (var elsewhere = services.CreateAsyncScope())
        {
            await elsewhere.ServiceProvider.GetRequiredService<WidgetsContext>().Database.ExecuteSqlRawAsync("UPDATE widgets SET name = 'washer'");
        }

        widget.Name = "rivet";

        // Act
        var rejection = await Assert.ThrowsAsync<LostRaceException>(() => unitOfWork.CommitAsync(CancellationToken.None));
        await unitOfWork.RollbackAsync(CancellationToken.None);

        // Assert
        await Assert.That(rejection!.Message).IsEqualTo("changed");
    }

    [Test]
    public async Task A_matching_rule_that_returns_nothing_leaves_the_failure_to_the_next_rule()
    {
        // Arrange
        await using var services = await ComposeAsync(map => map
            .On(NameKey, failure => null)
            .On(NameKey, failure => CommitResolution.Reject(new LostRaceException("second"))));
        await SeedAsync(services, "bolt");

        await using var scope = services.CreateAsyncScope();
        var unitOfWork = await BeginAsync(scope);
        scope.ServiceProvider.GetRequiredService<WidgetsContext>().Widgets.Add(new Widget { Id = FixedId(8), Name = "bolt" });

        // Act
        var rejection = await Assert.ThrowsAsync<LostRaceException>(() => unitOfWork.CommitAsync(CancellationToken.None));
        await unitOfWork.RollbackAsync(CancellationToken.None);

        // Assert
        await Assert.That(rejection!.Message).IsEqualTo("second");
    }

    [Test]
    public async Task The_first_rule_that_decides_wins_and_later_rules_never_run()
    {
        // Arrange
        var laterRuleRuns = 0;
        await using var services = await ComposeAsync(map => map
            .On(NameKey, failure => CommitResolution.Reject(new LostRaceException("first")))
            .On(NameKey, failure =>
            {
                laterRuleRuns++;
                return CommitResolution.Reject(new LostRaceException("second"));
            }));
        await SeedAsync(services, "bolt");

        await using var scope = services.CreateAsyncScope();
        var unitOfWork = await BeginAsync(scope);
        scope.ServiceProvider.GetRequiredService<WidgetsContext>().Widgets.Add(new Widget { Id = FixedId(9), Name = "bolt" });

        // Act
        var rejection = await Assert.ThrowsAsync<LostRaceException>(() => unitOfWork.CommitAsync(CancellationToken.None));
        await unitOfWork.RollbackAsync(CancellationToken.None);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(rejection!.Message).IsEqualTo("first");
            await Assert.That(laterRuleRuns).IsEqualTo(0);
        }
    }

    [Test]
    public async Task A_save_outside_the_unit_of_work_gets_the_same_rejection()
    {
        // Arrange
        await using var services = await ComposeAsync(map => map.On(NameKey, failure => CommitResolution.Reject(new LostRaceException("name taken"))));
        await SeedAsync(services, "bolt");

        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<WidgetsContext>();
        context.Widgets.Add(new Widget { Id = FixedId(4), Name = "bolt" });

        // Act
        var rejection = await Assert.ThrowsAsync<LostRaceException>(() => context.SaveChangesAsync());

        // Assert
        await Assert.That(rejection!.Message).IsEqualTo("name taken");
    }

    [Test]
    public async Task A_rule_the_unit_of_work_decides_runs_once_per_failed_save()
    {
        // Arrange
        var evaluations = 0;
        await using var services = await ComposeAsync(map => map.On(NameKey, failure =>
        {
            evaluations++;
            return CommitResolution.Reject(new LostRaceException("name taken"));
        }));
        await SeedAsync(services, "bolt");

        await using var scope = services.CreateAsyncScope();
        var unitOfWork = await BeginAsync(scope);
        scope.ServiceProvider.GetRequiredService<WidgetsContext>().Widgets.Add(new Widget { Id = FixedId(5), Name = "bolt" });

        // Act
        var rejection = await Assert.ThrowsAsync<LostRaceException>(() => unitOfWork.CommitAsync(CancellationToken.None));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(rejection).IsNotNull();
            await Assert.That(evaluations).IsEqualTo(1);
        }
    }

    [Test]
    public async Task A_failure_no_rule_claims_passes_through_unchanged()
    {
        // Arrange
        await using var services = await ComposeAsync(map => map.On("ux_other", failure => CommitResolution.Reject(new LostRaceException("name taken"))));
        await SeedAsync(services, "bolt");

        await using var scope = services.CreateAsyncScope();
        var unitOfWork = await BeginAsync(scope);
        scope.ServiceProvider.GetRequiredService<WidgetsContext>().Widgets.Add(new Widget { Id = FixedId(6), Name = "bolt" });

        // Act
        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => unitOfWork.CommitAsync(CancellationToken.None));
        await unitOfWork.RollbackAsync(CancellationToken.None);

        // Assert
        await Assert.That(((PostgresException)failure!.InnerException!).ConstraintName).IsEqualTo(NameKey);
    }

    [Test]
    public async Task A_unit_without_rules_lets_the_storage_failure_through()
    {
        // Arrange
        await using var services = await ComposeAsync(rules: null);
        await SeedAsync(services, "bolt");

        await using var scope = services.CreateAsyncScope();
        var unitOfWork = await BeginAsync(scope);
        scope.ServiceProvider.GetRequiredService<WidgetsContext>().Widgets.Add(new Widget { Id = FixedId(7), Name = "bolt" });

        // Act
        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => unitOfWork.CommitAsync(CancellationToken.None));
        await unitOfWork.RollbackAsync(CancellationToken.None);

        // Assert
        await Assert.That(((PostgresException)failure!.InnerException!).ConstraintName).IsEqualTo(NameKey);
    }

    [Test]
    public async Task A_rule_that_repairs_the_staged_changes_has_the_save_run_again()
    {
        // Arrange
        await using var services = await ComposeAsync(map => map.OnAsync(NameKey, async failure =>
        {
            var duplicates = failure.Context.ChangeTracker.Entries<Widget>().Where(e => e.State == EntityState.Added && e.Entity.Name == "bolt").ToArray();
            foreach (var duplicate in duplicates)
            {
                duplicate.State = EntityState.Detached;
            }

            await Task.CompletedTask;
            return CommitResolution.Repair;
        }));
        await SeedAsync(services, "bolt");

        // Act
        await using (var scope = services.CreateAsyncScope())
        {
            var unitOfWork = await BeginAsync(scope);
            scope.ServiceProvider.GetRequiredService<WidgetsContext>().Widgets.AddRange(
                new Widget { Id = FixedId(8), Name = "nut" }, new Widget { Id = FixedId(9), Name = "bolt" });
            await unitOfWork.CommitAsync(CancellationToken.None);
        }

        // Assert
        await Assert.That(await CountAsync(services)).IsEqualTo(2);
    }

    [Test]
    public async Task A_rule_that_keeps_asking_to_repair_gives_up_with_a_retryable_conflict_after_the_repair_limit()
    {
        // Arrange
        var evaluations = 0;
        await using var services = await ComposeAsync(map => map.On(NameKey, failure =>
        {
            evaluations++;
            return CommitResolution.Repair;
        }));
        await SeedAsync(services, "bolt");

        await using var scope = services.CreateAsyncScope();
        var unitOfWork = await BeginAsync(scope);
        scope.ServiceProvider.GetRequiredService<WidgetsContext>().Widgets.Add(new Widget { Id = FixedId(10), Name = "bolt" });

        // Act
        var conflict = await Assert.ThrowsAsync<CommitConflictException>(() => unitOfWork.CommitAsync(CancellationToken.None));
        await unitOfWork.RollbackAsync(CancellationToken.None);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(evaluations).IsEqualTo(4);
            await Assert.That(conflict!.Answer).IsTypeOf<ConflictError>();
            await Assert.That(conflict.Answer.Code).IsEqualTo(ICommitRejection.CommitConflictCode);
            await Assert.That(await CountAsync(services)).IsEqualTo(1);
        }
    }

    [Test]
    public async Task A_deadlock_is_the_unavailable_rejection_of_the_postgres_rule()
    {
        // Arrange
        await using var services = await ComposeAsync(map => map
            .OnPostgres(failure => failure.Postgres?.SqlState == PostgresErrorCodes.DeadlockDetected
                ? CommitResolution.Reject(new BusyException("deadlock"))
                : null)
            .OnConcurrency(failure => CommitResolution.Reject(new LostRaceException("changed"))));
        await SeedCodedAsync(services, "A");
        await using (var seeding = services.CreateAsyncScope())
        {
            var seedUnit = await BeginAsync(seeding);
            seeding.ServiceProvider.GetRequiredService<WidgetsContext>().Widgets.Add(new Widget { Id = FixedId(107), Name = "stored B", Code = "B" });
            await seedUnit.CommitAsync(CancellationToken.None);
        }

        await using var first = services.CreateAsyncScope();
        await using var second = services.CreateAsyncScope();
        var firstContext = first.ServiceProvider.GetRequiredService<WidgetsContext>();
        var secondContext = second.ServiceProvider.GetRequiredService<WidgetsContext>();
        await using var firstOwner = await firstContext.Database.BeginTransactionAsync();
        await using var secondOwner = await secondContext.Database.BeginTransactionAsync();
        var firstUnit = await BeginAsync(first);
        var secondUnit = await BeginAsync(second);
        var own = await firstContext.Widgets.SingleAsync(w => w.Id == FixedId(100));
        var other = await firstContext.Widgets.SingleAsync(w => w.Id == FixedId(107));
        var secondOwn = await secondContext.Widgets.SingleAsync(w => w.Id == FixedId(107));
        var secondOther = await secondContext.Widgets.SingleAsync(w => w.Id == FixedId(100));
        await firstContext.Database.ExecuteSqlRawAsync("SELECT 1 FROM widgets WHERE \"Id\" = {0} FOR UPDATE", FixedId(100));
        await secondContext.Database.ExecuteSqlRawAsync("SELECT 1 FROM widgets WHERE \"Id\" = {0} FOR UPDATE", FixedId(107));
        (own.Name, other.Name) = ("first own", "first other");
        (secondOwn.Name, secondOther.Name) = ("second own", "second other");

        // Act
        var outcomes = await Task.WhenAll(CommitAsync(firstUnit), CommitAsync(secondUnit));

        // Assert
        var rejections = outcomes.OfType<BusyException>().ToArray();
        using (Assert.Multiple())
        {
            await Assert.That(outcomes.Count(outcome => outcome is null)).IsEqualTo(1);
            await Assert.That(rejections.Length).IsEqualTo(1);
            await Assert.That(rejections[0].Answer).IsTypeOf<UnavailableError>();
        }

        static async Task<Exception?> CommitAsync(IUnitOfWork unitOfWork)
        {
            try
            {
                await unitOfWork.CommitAsync(CancellationToken.None);
                return null;
            }
            catch (BusyException busy)
            {
                // The loser rolls back to its savepoint, which frees the row it locked and lets the other commit.
                await unitOfWork.RollbackAsync(CancellationToken.None);
                return busy;
            }
        }
    }

    [Test]
    public async Task A_commit_conflict_answers_a_conflict_with_the_code_the_http_layer_recognises()
    {
        // Arrange
        var conflict = new CommitConflictException();

        // Act
        var answer = ((ICommitRejection)conflict).Answer;

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(answer).IsTypeOf<ConflictError>();
            await Assert.That(answer.Code).IsEqualTo(ICommitRejection.CommitConflictCode);
            await Assert.That(ICommitRejection.CommitConflictCode).IsEqualTo("platform.persistence.commit_conflict");
        }
    }

    [Test]
    public async Task A_module_context_is_pooled_and_comes_back_clean()
    {
        // Arrange
        await using var services = await ComposeAsync(rules: null);

        WidgetsContext first;
        await using (var scope = services.CreateAsyncScope())
        {
            first = scope.ServiceProvider.GetRequiredService<WidgetsContext>();
            first.Widgets.Add(new Widget { Id = FixedId(11), Name = "staged, never saved" });
        }

        await using var next = services.CreateAsyncScope();

        // Act
        var second = next.ServiceProvider.GetRequiredService<WidgetsContext>();

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(second).IsSameReferenceAs(first);
            await Assert.That(second.ChangeTracker.Entries().Count()).IsEqualTo(0);
        }
    }

    /// <summary>What a module throws when someone else changed the same data first: an expected answer, not an incident.</summary>
    public sealed class LostRaceException(string message) : Exception(message), ICommitRejection
    {
        public Error Answer { get; } = new ConflictError("test.changed_concurrently");
    }

    /// <summary>What a module throws when the storage could not take the commit now: the caller may retry.</summary>
    public sealed class BusyException(string message) : Exception(message), ICommitRejection
    {
        public Error Answer { get; } = new UnavailableError("test.busy");
    }

    /// <summary>A widget was added.</summary>
    private sealed record WidgetAdded(Guid Id) : IDomainEvent;

    /// <summary>Notes the SQL the context sends and the after-commit events it handled.</summary>
    private sealed class Recorder : DbCommandInterceptor, IAfterCommitDomainEventHandler<WidgetAdded>
    {
        public List<string> Commands { get; } = [];

        public List<Guid> AfterCommit { get; } = [];

        public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
        {
            Commands.Add(command.CommandText);
            return result;
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }

        public Task HandleAsync(WidgetAdded domainEvent, CancellationToken cancellationToken)
        {
            AfterCommit.Add(domainEvent.Id);
            return Task.CompletedTask;
        }
    }

    /// <summary>A row with a unique name and a deferrable unique code, to provoke constraint failures.</summary>
    private sealed class Widget : AggregateRoot
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string? Code { get; set; }

        public void Announce() => Raise(new WidgetAdded(Id));
    }

    /// <summary>The database context of the widgets.</summary>
    private sealed class WidgetsContext(DbContextOptions<WidgetsContext> options) : DbContext(options)
    {
        public DbSet<Widget> Widgets => Set<Widget>();

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<Widget>(widget =>
            {
                widget.ToTable("widgets");
                widget.Ignore(w => w.DomainEvents);
                widget.Property(w => w.Name).HasColumnName("name");
                widget.Property(w => w.Code).HasColumnName("code");
                widget.HasIndex(w => w.Name).IsUnique().HasDatabaseName(NameKey);
                widget.Property<uint>("xmin").HasColumnName("xmin").IsRowVersion();
            });
    }
}
