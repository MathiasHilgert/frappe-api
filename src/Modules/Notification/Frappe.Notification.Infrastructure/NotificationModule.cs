using System.Reflection;
using Frappe.Modularity;
using Frappe.Notification.Application;
using Frappe.Notification.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Frappe.Notification.Infrastructure;

public sealed class NotificationModule : IModule
{
    public string Name => "notification";

    public IReadOnlyList<Assembly> MessagingAssemblies { get; } =
        [typeof(NotificationApplicationMarker).Assembly, typeof(NotificationContractsMarker).Assembly];

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ResendOptions>()
            .Bind(configuration.GetSection(ResendOptions.Section))
            .Validate(options => options.BaseAddress.IsAbsoluteUri, $"'{ResendOptions.Section}:BaseAddress' must be absolute.")
            .ValidateOnStart();

        // A configured key always means real mail. Without one, Development logs mail instead; anywhere else Resend
        // rejects the call, so the first mail fails loudly (and is retried) rather than vanishing.
        // Decided here, not in a factory: Wolverine generates handler code from concrete registrations.
        var hasKey = configuration.GetSection(ResendOptions.Section).Get<ResendOptions>()?.HasApiKey ?? false;
        var isDevelopment = string.Equals(configuration[HostDefaults.EnvironmentKey], Environments.Development,
            StringComparison.OrdinalIgnoreCase);
        if (!hasKey && isDevelopment)
        {
            services.AddSingleton<IMailer, LoggingMailer>();
            return;
        }

        services.AddHttpClient<IMailer, ResendMailer>((sp, http) =>
            http.BaseAddress = sp.GetRequiredService<IOptions<ResendOptions>>().Value.BaseAddress);
    }
}
