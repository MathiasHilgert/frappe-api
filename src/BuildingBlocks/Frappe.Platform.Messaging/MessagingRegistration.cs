using Frappe.Modularity;
using Frappe.SharedKernel.Domain;
using JasperFx;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quartz;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Postgresql;

namespace Frappe.Platform.Messaging;

public static class MessagingRegistration
{
    public const string Schema = "wolverine";

    /// <summary>
    /// Wolverine as mediator and durable bus: domain events are dequeued from saved aggregates and written to the
    /// outbox in the same transaction; local queues are durable, so reactions survive crashes and are retried.
    /// </summary>
    public static IHostApplicationBuilder AddMessaging(
        this IHostApplicationBuilder builder,
        string postgresConnectionString,
        IReadOnlyList<IModule> modules,
        params IReadOnlyList<System.Reflection.Assembly> platformMessagingAssemblies)
    {
        builder.UseWolverine(opts =>
        {
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
    /// Recurring jobs. In-memory store until the first real recurring job lands; switch to the clustered
    /// PostgreSQL store (schema "quartz") then, so only one instance fires each trigger.
    /// </summary>
    public static IServiceCollection AddJobs(this IServiceCollection services)
    {
        services.AddQuartz();
        services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);
        return services;
    }
}
