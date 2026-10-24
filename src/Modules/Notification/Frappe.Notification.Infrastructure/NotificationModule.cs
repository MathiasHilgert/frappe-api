using System.Reflection;
using Frappe.Modularity;
using Frappe.Notification.Application;
using Frappe.Notification.Application.Mails;
using Frappe.Notification.Application.OwnerCodeMails;
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
using Wolverine.Runtime;

namespace Frappe.Notification.Infrastructure;

public sealed class NotificationModule : IModule
{
    public string Name => "notification";

    public IReadOnlyList<Assembly> MessagingAssemblies { get; } =
        [typeof(NotificationApplicationMarker).Assembly, typeof(NotificationContractsMarker).Assembly];

    public IReadOnlyList<string> Meters { get; } = [NotificationMetrics.MeterName];

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddMessageCatalog(typeof(NotificationModule).Assembly);
        services.AddSingleton<IOwnerCodeMails, CatalogOwnerCodeMails>();
        services.AddSingleton<NotificationMetrics>();

        // Only Development may run without a key (mail is logged instead); anywhere else a missing key fails the
        // start, not the first mail. Decided here, not in a factory: Wolverine generates handler code from concrete
        // registrations.
        var isDevelopment = string.Equals(configuration[HostDefaults.EnvironmentKey], Environments.Development,
            StringComparison.OrdinalIgnoreCase);
        services.AddSingleton<IValidateOptions<ResendOptions>, ResendOptionsValidator>();
        services.AddOptions<ResendOptions>()
            .BindConfiguration(ResendOptions.SectionName)
            .Validate(options => options.BaseAddress.IsAbsoluteUri, $"'{ResendOptions.SectionName}:BaseAddress' must be absolute.")
            .Validate(options => options.HasApiKey || isDevelopment,
                $"'{ResendOptions.SectionName}:ApiKey' is required outside Development.")
            .ValidateOnStart();

        var hasKey = configuration.GetSection(ResendOptions.SectionName).Get<ResendOptions>()?.HasApiKey ?? false;
        if (!hasKey && isDevelopment)
        {
            services.AddSingleton<IMailer, LoggingMailer>();
            return;
        }

        services.AddHttpClient<IMailer, ResendMailer>((sp, http) =>
        {
            var options = sp.GetRequiredService<IOptions<ResendOptions>>().Value;
            http.BaseAddress = options.BaseAddress;
            http.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        });
    }

    public void ConfigureMessaging(WolverineOptions options)
    {
        // The Resend mailer is a typed HttpClient, an opaque factory registration Wolverine cannot build inline,
        // and the mail composer depends on the platform's internal catalogue.
        options.CodeGeneration.AlwaysUseServiceLocationFor<IMailer>();
        options.CodeGeneration.AlwaysUseServiceLocationFor<IOwnerCodeMails>();

        // Only the mailer throws these, so the rules apply to the notification handlers alone. A rejected mail can
        // never succeed: straight to the dead-letter queue. A failed delivery backs off, then dead-letters. Either way
        // the dead letter is metered, so a mail that will never arrive shows on a dashboard.
        options.Policies.OnException<MailRejectedException>()
            .CustomAction((runtime, lifecycle, error) => DeadLetterAsync(runtime, lifecycle, error, "rejected"),
                "Dead-letter a rejected mail");
        options.Policies.OnException<MailDeliveryFailedException>()
            .RetryWithCooldown(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5))
            .Then.ScheduleRetry(TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(10))
            .Then.CustomAction((runtime, lifecycle, error) => DeadLetterAsync(runtime, lifecycle, error, "exhausted"),
                "Dead-letter an undeliverable mail");
    }

    private static async ValueTask DeadLetterAsync(
        IWolverineRuntime runtime, IEnvelopeLifecycle lifecycle, Exception error, string reason)
    {
        runtime.Services.GetRequiredService<NotificationMetrics>().DeadLettered(reason);
        await lifecycle.MoveToDeadLetterQueueAsync(error);
        // What the built-in MoveToErrorQueue also does: Wolverine's own dead-letter metrics and message tracking.
        runtime.MessageTracking.MovedToErrorQueue(lifecycle.Envelope!, error);
    }
}
