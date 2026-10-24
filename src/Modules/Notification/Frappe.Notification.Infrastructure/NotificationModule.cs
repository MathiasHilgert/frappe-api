using System.Reflection;
using Frappe.Modularity;
using Frappe.Notification.Application;
using Frappe.Notification.Contracts;
using Frappe.Notification.Infrastructure.Mails;
using Frappe.Notification.Infrastructure.Resend;
using Frappe.Platform.Localization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Wolverine;
using Wolverine.ErrorHandling;

namespace Frappe.Notification.Infrastructure;

public sealed class NotificationModule : IModule
{
    public string Name => "notification";

    public IReadOnlyList<Assembly> MessagingAssemblies { get; } =
        [typeof(NotificationApplicationMarker).Assembly, typeof(NotificationContractsMarker).Assembly];

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddMessageCatalog(typeof(NotificationModule).Assembly);
        services.AddSingleton<IOwnerCodeMails, CatalogOwnerCodeMails>();

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

    public void ConfigureMessaging(WolverineOptions options)
    {
        // The Resend mailer is a typed HttpClient, an opaque factory registration Wolverine cannot build inline,
        // and the mail composer depends on the platform's internal catalogue.
        options.CodeGeneration.AlwaysUseServiceLocationFor<IMailer>();
        options.CodeGeneration.AlwaysUseServiceLocationFor<IOwnerCodeMails>();

        // Only the mailer throws these, so the rules apply to the notification handlers alone. A rejected mail can
        // never succeed: straight to the dead-letter queue. A failed delivery backs off, then dead-letters.
        options.Policies.OnException<MailRejectedException>().MoveToErrorQueue();
        options.Policies.OnException<MailDeliveryFailedException>()
            .RetryWithCooldown(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5))
            .Then.ScheduleRetry(TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(10))
            .Then.MoveToErrorQueue();
    }
}
