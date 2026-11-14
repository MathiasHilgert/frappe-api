using Frappe.Health;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using Quartz;

namespace Frappe.Scheduling;

/// <summary>
/// Clustered Quartz.NET on Postgres: every Worker replica shares one job store in schema <c>quartz</c>, so each
/// trigger fires on exactly one replica. Its tables are created by the Migrator (with
/// <c>provisionSchema</c>), never by a Worker at runtime: Workers connect with a role that cannot run DDL.
/// </summary>
public static class SchedulingRegistration
{
    public const string Schema = "quartz";

    public const string TablePrefix = Schema + ".qrtz_";

    /// <summary>The login role the Worker's scheduler uses.</summary>
    public const string Role = "frappe_scheduler";

    /// <summary>
    /// Service key of the job store's <see cref="NpgsqlDataSource"/>: one per container, built from
    /// <see cref="SchedulingOptions.ConnectionString"/> and disposed with it, keyed so it never stands in for another
    /// data source the host registers.
    /// </summary>
    public const string DataSourceKey = "frappe-scheduler";

    /// <summary>
    /// The clustered scheduler, bound from <c>ConnectionStrings:Scheduler</c> (required) and
    /// <c>Scheduling:ProvisionSchema</c> (<see cref="SchedulingOptions"/>), with the jobs <paramref name="jobs"/> adds.
    /// A missing connection string fails at startup naming the key.
    /// </summary>
    public static IServiceCollection AddFrappeScheduling(this IServiceCollection services, Action<IQuartzBuilder> jobs)
    {
        services.AddOptions<SchedulingOptions>()
            .BindConfiguration(SchedulingOptions.Section)
            .Configure<IConfiguration>((options, configuration) =>
                options.ConnectionString = configuration.GetConnectionString(SchedulingOptions.ConnectionStringName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.ConnectionString), $"Configuration 'ConnectionStrings:{SchedulingOptions.ConnectionStringName}' is required.")
            .ValidateOnStart();
        return services.AddFrappeSchedulingServices(jobs);
    }

    /// <summary>The same as <see cref="AddFrappeScheduling(IServiceCollection, Action{IQuartzBuilder})"/>, with the settings given in code.</summary>
    public static IServiceCollection AddFrappeScheduling(
        this IServiceCollection services,
        string connectionString,
        Action<IQuartzBuilder> jobs,
        bool provisionSchema = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        services.AddOptions<SchedulingOptions>().Configure(options =>
        {
            options.ConnectionString = connectionString;
            options.ProvisionSchema = provisionSchema;
        });
        return services.AddFrappeSchedulingServices(jobs);
    }

    private static IServiceCollection AddFrappeSchedulingServices(this IServiceCollection services, Action<IQuartzBuilder> jobs)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        services.AddKeyedSingleton(DataSourceKey, (provider, _) => NpgsqlDataSource.Create(Settings(provider).ConnectionString!));
        services.AddQuartz(quartz =>
        {
            quartz.ConfigureScheduler(scheduler => scheduler.InstanceName = "frappe");
            quartz.UsePersistentStore(store =>
            {
                // Quartz runs the factory once per scheduler and never disposes what it returns: the container owns it.
                store.UsePostgres(source => source.DataSourceFactory = provider => provider.GetRequiredKeyedService<NpgsqlDataSource>(DataSourceKey));
                store.ConfigureStore(options => options.TablePrefix = TablePrefix);
                store.UseClustering(_ => { });
            });
            jobs(quartz);
        });

        // After AddQuartz, so it runs after the store's own configuration: provisioning is read from the options.
        services.AddSingleton<IConfigureOptions<AdoJobStoreOptions>>(provider => new ConfigureNamedOptions<AdoJobStoreOptions>(null, store =>
        {
            if (Settings(provider).ProvisionSchema)
            {
                store.SchemaProvisioning = SchemaProvisioning.CreateIfMissing;
            }
        }));
        services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);
        services.AddCriticalDependency(
            "scheduler-database",
            provider => new DatabaseHealthCheck(() => new NpgsqlConnection(Settings(provider).ConnectionString)));
        return services;
    }

    private static SchedulingOptions Settings(IServiceProvider provider) => provider.GetRequiredService<IOptions<SchedulingOptions>>().Value;

    /// <summary>
    /// Creates the <c>quartz</c> schema if it does not exist. Run by the Migrator with the owner role, before a
    /// scheduler with <c>provisionSchema</c> creates the tables inside it.
    /// </summary>
    public static async Task EnsureSchemaAsync(string connectionString, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand($"CREATE SCHEMA IF NOT EXISTS {Schema}", connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Creates the <c>quartz</c> schema and the Quartz tables in it, without starting a scheduler. Run by the Migrator
    /// with the owner role.
    /// </summary>
    public static async Task ProvisionAsync(string ownerConnectionString, CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(ownerConnectionString, cancellationToken);

        await using var services = new ServiceCollection()
            .AddFrappeScheduling(ownerConnectionString, _ => { }, provisionSchema: true)
            .BuildServiceProvider();
        var scheduler = await services.GetRequiredService<ISchedulerFactory>().GetScheduler(cancellationToken);
        await scheduler.Shutdown(waitForJobsToComplete: false, cancellationToken);
    }
}
