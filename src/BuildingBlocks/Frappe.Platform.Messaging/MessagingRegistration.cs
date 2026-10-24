using Frappe.Application.Abstractions.Messaging;
using Frappe.Modularity;
using Frappe.Platform.Messaging.Jobs;
using Frappe.Platform.Messaging.Sealing;
using Frappe.SharedKernel.Domain;
using JasperFx;
using JasperFx.CodeGeneration;
using JasperFx.Resources;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Quartz;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Postgresql;

namespace Frappe.Platform.Messaging;

public static class MessagingRegistration
{
    public const string Schema = "wolverine";

    /// <summary>Overrides how handler code is loaded (Dynamic | Auto | Static); integration tests force Static.</summary>
    public const string TypeLoadModeSetting = "Wolverine:TypeLoadMode";

    /// <summary>
    /// Wolverine as mediator and durable bus: domain events are dequeued from saved aggregates and written to the
    /// outbox in the same transaction; local queues are durable, so reactions survive crashes and are retried.
    /// </summary>
    public static IHostApplicationBuilder AddMessaging(
        this IHostApplicationBuilder builder,
        string postgresConnectionString,
        IReadOnlyList<IModule> modules,
        System.Reflection.Assembly applicationAssembly,
        params IReadOnlyList<System.Reflection.Assembly> platformMessagingAssemblies)
    {
        builder.Services.AddSingleton<IValidateOptions<MessageSealOptions>, MessageSealOptionsValidator>();
        // The development key is committed, so it is public: outside Development it would seal nothing.
        builder.Services.AddOptions<MessageSealOptions>()
            .BindConfiguration(MessageSealOptions.SectionName)
            .Validate<IHostEnvironment>(
                (options, environment) => environment.IsDevelopment()
                    || !string.Equals(options.Key, MessageSealOptions.DevelopmentKey, StringComparison.Ordinal),
                $"'{MessageSealOptions.SectionName}:Key' is the committed development key; set a real one outside Development.")
            .ValidateOnStart();
        builder.Services.AddSingleton<IMessageSeal, AesGcmMessageSeal>();

        builder.UseWolverine(opts =>
        {
            // Handler code is generated ahead of time (`just codegen`) and compiled into the host. Development
            // regenerates it when stale; everywhere else a missing or stale handler fails startup instead of
            // compiling with Roslyn on the first message.
            opts.ApplicationAssembly = applicationAssembly;
            opts.CodeGeneration.TypeLoadMode = builder.Configuration.GetValue(
                TypeLoadModeSetting,
                builder.Environment.IsDevelopment() ? TypeLoadMode.Auto : TypeLoadMode.Static);

            // Wolverine keeps its own pool: its envelope storage is configured before the container exists.
            opts.PersistMessagesWithPostgresql(postgresConnectionString, Schema);
            opts.UseEntityFrameworkCoreTransactions();
            opts.PublishDomainEventsFromEntityFrameworkCore<IHasDomainEvents, IDomainEvent>(
                aggregate => aggregate.DequeueDomainEvents());

            opts.Policies.AutoApplyTransactions();
            opts.Policies.UseDurableLocalQueues();
            opts.Policies.UseDurableInboxOnAllListeners();
            opts.Policies.UseDurableOutboxOnAllSendingEndpoints();

            // Several modules may react to the same event: each handler gets its own queue, retries and failures,
            // so one module's outage never blocks or replays another module's reaction.
            opts.MultipleHandlerBehavior = MultipleHandlerBehavior.Separated;

            foreach (var assembly in platformMessagingAssemblies)
                opts.Discovery.IncludeAssembly(assembly);

            foreach (var module in modules)
            {
                foreach (var assembly in module.MessagingAssemblies)
                    opts.Discovery.IncludeAssembly(assembly);
                module.ConfigureMessaging(opts);
            }

            opts.AutoBuildMessageStorageOnStartup = builder.Environment.IsDevelopment()
                ? AutoCreate.CreateOrUpdate
                : AutoCreate.None;
        });

        return builder;
    }

    /// <summary>
    /// Recurring jobs on Quartz's clustered PostgreSQL store (schema <c>quartz</c>): every instance runs a scheduler,
    /// but the store's row locks let exactly one of them fire each trigger, and a dead instance's jobs are recovered
    /// by another. The schema is a JasperFx resource (<c>resources setup</c>); Development provisions it on start.
    /// </summary>
    public static IServiceCollection AddJobs(
        this IServiceCollection services, string postgresConnectionString, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        var schema = new QuartzSchema(postgresConnectionString);
        services.AddSingleton(schema);
        services.AddSingleton<IStatefulResource>(schema);
        // Registered before the Quartz hosted service, so it runs first.
        services.AddHostedService(_ => new QuartzSchemaSetup(schema, provision: environment.IsDevelopment()));

        services.AddQuartz(quartz => quartz.UsePersistentStore(store =>
        {
            store.UseClustering();
            store.UseSystemTextJsonSerializer();
            store.UsePostgres(postgresConnectionString);
            store.ConfigureStore(options => options.TablePrefix = QuartzSchema.TablePrefix);
        }));
        services.Configure<QuartzSchedulerOptions>(options => options.GenerateInstanceId = true);
        services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);
        return services;
    }
}
