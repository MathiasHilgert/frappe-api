using Frappe.Health;
using Frappe.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
    extension(IServiceCollection services)
    {
        /// <summary>
        /// The same as <see cref="AddFrappeScheduling(IServiceCollection, Action{IQuartzBuilder})"/>, with the settings given
        /// in code and the default connection limits (no configuration is read).
        /// </summary>
        public IServiceCollection AddFrappeScheduling(string connectionString,
            Action<IQuartzBuilder> jobs,
            bool provisionSchema = false)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
            services.AddOptions<SchedulingOptions>().Configure(options =>
            {
                options.ConnectionString = connectionString;
                options.ProvisionSchema = provisionSchema;
            });
            services.AddOptions<DatabaseOptions>();
            return services.AddFrappeSchedulingServices(jobs);
        }

        /// <summary>
        /// The clustered scheduler, bound from <c>ConnectionStrings:Scheduler</c> (required) and
        /// <c>Scheduling:ProvisionSchema</c> (<see cref="SchedulingOptions"/>), with the jobs <paramref name="jobs"/> adds.
        /// Its job store connects under the same limits as every module database (<c>Database:*</c>,
        /// <see cref="DatabaseOptions"/>). A missing connection string or an invalid limit fails at startup naming the key.
        /// </summary>
        public IServiceCollection AddFrappeScheduling(Action<IQuartzBuilder> jobs)
        {
            services.AddOptions<SchedulingOptions>()
                .BindConfiguration(SchedulingOptions.Section)
                .Configure<IConfiguration>((options, configuration) =>
                    options.ConnectionString = configuration.GetConnectionString(SchedulingOptions.ConnectionStringName))
                .ValidateOnStart();
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<SchedulingOptions>, SchedulingOptionsValidator>());
            services.AddDatabaseOptions();
            return services.AddFrappeSchedulingServices(jobs);
        }

        private IServiceCollection AddFrappeSchedulingServices(Action<IQuartzBuilder> jobs)
        {
            ArgumentNullException.ThrowIfNull(jobs);
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IDatabaseParticipant, SchedulingDatabase>());
            services.AddKeyedSingleton(DataSourceKey, (provider, _) => NpgsqlDataSource.Create(ModuleDatabase.WithConnectionLimits(
                Settings(provider).ConnectionString!,
                provider.GetRequiredService<IOptions<DatabaseOptions>>().Value)));
            services.AddQuartz(quartz =>
            {
                quartz.ConfigureScheduler(scheduler => scheduler.InstanceName = "frappe");
                quartz.UsePersistentStore(store =>
                {
                    // Quartz runs the factory once per scheduler and never disposes what it returns: the container owns it.
                    store.UsePostgres(source =>
                        source.DataSourceFactory = provider => provider.GetRequiredKeyedService<NpgsqlDataSource>(DataSourceKey));
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
            // From the job store's own data source: one bounded pool for the scheduler, the readiness check included.
            services.AddCriticalDependency(
                "scheduler-database",
                provider => new DatabaseHealthCheck(() => provider.GetRequiredKeyedService<NpgsqlDataSource>(DataSourceKey).CreateConnection()));
            services.AddCriticalDependency("scheduler", provider => new SchedulerHealthCheck(provider.GetRequiredService<ISchedulerFactory>()));
            return services;
        }
    }

    /// <summary>The Postgres schema of the job store.</summary>
    public const string Schema = "quartz";

    /// <summary>The prefix of every Quartz table, schema included.</summary>
    public const string TablePrefix = Schema + ".qrtz_";

    /// <summary>The login role the Worker's scheduler uses.</summary>
    public const string Role = "frappe_scheduler";

    /// <summary>
    /// Service key of the job store's <see cref="NpgsqlDataSource"/>: one per container, built from
    /// <see cref="SchedulingOptions.ConnectionString"/> and disposed with it, keyed so it never stands in for another
    /// data source the host registers.
    /// </summary>
    public const string DataSourceKey = "frappe-scheduler";

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
