using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Frappe.Health;

/// <summary>
/// Health for a process that serves no HTTP (the Worker), the model of Microsoft's Kubernetes health check
/// publisher: every few seconds the checks run, and each probe's port is open only while that probe is healthy.
/// A closed port is what a <c>tcpSocket</c> probe reads as a failure.
/// </summary>
public static class TcpHealthProbes
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// The probes on the ports of <c>Health:Probes</c> (<see cref="TcpProbeOptions"/>); a port outside 1 to 65535
        /// fails at startup naming the key.
        /// </summary>
        public IServiceCollection AddTcpHealthProbes()
        {
            ArgumentNullException.ThrowIfNull(services);
            services.AddOptions<TcpProbeOptions>().BindConfiguration(TcpProbeOptions.Section).ValidateOnStart();
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<TcpProbeOptions>, TcpProbeOptionsValidator>());
            return services.AddTcpHealthProbeServices();
        }

        /// <summary>The same as <see cref="AddTcpHealthProbes(IServiceCollection)"/>, with the ports given in code.</summary>
        public IServiceCollection AddTcpHealthProbes(TcpProbePorts ports)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(ports);
            services.AddOptions<TcpProbeOptions>().Configure(options =>
            {
                options.LivePort = ports.Live;
                options.ReadyPort = ports.Ready;
                options.StartupPort = ports.Startup;
            });
            return services.AddTcpHealthProbeServices();
        }

        private IServiceCollection AddTcpHealthProbeServices()
        {
            services.AddFrappeHealth();
            services.Configure<HealthCheckPublisherOptions>(options =>
            {
                options.Delay = TimeSpan.FromSeconds(1);
                options.Period = TimeSpan.FromSeconds(5);
                options.Timeout = HealthRegistration.CheckTimeout * 2;
            });
            services.AddSingleton(provider => Ports(provider.GetRequiredService<IOptions<TcpProbeOptions>>().Value));
            services.AddSingleton<IHealthCheckPublisher, TcpProbePublisher>();
            return services;
        }
    }

    private static TcpProbePorts Ports(TcpProbeOptions options) => new(options.LivePort, options.ReadyPort, options.StartupPort);
}
