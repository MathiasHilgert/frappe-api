using Frappe.Health;
using Frappe.Messaging.Abstractions;
using Frappe.Messaging.JetStream;
using Frappe.Messaging.Relay;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NATS.Client.Core;
using NATS.Client.JetStream;

namespace Frappe.Messaging.Hosting;

/// <summary>
/// Adds the JetStream transport to a host, the Worker. It materializes what modules declared with <see cref="MessagingDeclarations"/>
/// (relays, consumers, topology, purgers), whether they were declared before or after this call.
/// </summary>
public static class MessagingRegistration
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// One NATS connection per process, its JetStream context and the publisher the relays use, bound from
        /// <c>ConnectionStrings:Nats</c> (required) and <c>Messaging:Namespace</c> (optional): every stream and subject of the
        /// process follows one <see cref="JetStreamNaming"/>, the production names unless a namespace is given (used to
        /// isolate several topologies on one server, as tests do). A missing URL fails at startup naming the key.
        /// </summary>
        /// <param name="clientName">The connection name the server shows.</param>
        public IServiceCollection AddJetStream(string clientName)
        {
            services.AddOptions<JetStreamOptions>()
                .BindConfiguration(JetStreamOptions.Section)
                .Configure<IConfiguration>(
                    (options, configuration) => options.Url = configuration.GetConnectionString(JetStreamOptions.ConnectionStringName))
                .ValidateOnStart();
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<JetStreamOptions>, JetStreamOptionsValidator>());
            return services.AddJetStreamServices(clientName);
        }

        /// <summary>The same as <see cref="AddJetStream(IServiceCollection, string)"/>, with the settings given in code.</summary>
        /// <param name="url">The NATS server URL.</param>
        /// <param name="clientName">The connection name the server shows.</param>
        /// <param name="namespace">An optional prefix for every stream and subject; null or empty keeps the production names.</param>
        public IServiceCollection AddJetStream(string url, string clientName, string? @namespace = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(url);
            services.AddOptions<JetStreamOptions>().Configure(options =>
            {
                options.Url = url;
                options.Namespace = @namespace;
            });
            return services.AddJetStreamServices(clientName);
        }

        private IServiceCollection AddJetStreamServices(string clientName)
        {
            services.TryAddSingleton(TimeProvider.System);
            services.TryAddSingleton(provider => provider.GetRequiredService<IOptions<JetStreamOptions>>().Value.Namespace is { Length: > 0 } prefix
                ? new JetStreamNaming(prefix)
                : JetStreamNaming.Default);
            services.TryAddSingleton(provider => new NatsConnection(new NatsOpts
            {
                Url = provider.GetRequiredService<IOptions<JetStreamOptions>>().Value.Url!,
                Name = clientName,
            }));
            services.TryAddSingleton<INatsJSContext>(provider => new NatsJSContext(provider.GetRequiredService<NatsConnection>()));
            services.AddCriticalDependency("nats", provider => new NatsHealthCheck(provider.GetRequiredService<NatsConnection>()));
            services.TryAddSingleton<IMessagePublisher, JetStreamPublisher>();
            MessagingTransport.Attach(services, new JetStreamMaterializer(services));
            return services;
        }
    }


    /// <summary>
    /// The list configured under <paramref name="section"/>:<paramref name="key"/>, or <see langword="null"/> when
    /// none is. The binder appends configured items to a list property's default instead of replacing it, so a list
    /// with a default is read again here and assigned whole.
    /// </summary>
    internal static TimeSpan[]? ConfiguredList(IConfiguration configuration, string section, string key)
    {
        var list = configuration.GetSection(section).GetSection(key);
        return list.GetChildren().Any() ? list.Get<TimeSpan[]>() : null;
    }
}
