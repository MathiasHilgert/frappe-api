using Frappe.Localization;
using Frappe.Notifications.Application.Messages;
using Frappe.Notifications.Application.Senders;
using Frappe.Notifications.Domain.Deliveries;
using Frappe.Notifications.Messages.Account;
using Frappe.Notifications.Messages.Marketing;
using Frappe.Notifications.Senders.Email;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Frappe.Notifications.UnitTests.Emails;

/// <summary>The email channel, the four kinds of the module and a recording sender, composed as a host composes them.</summary>
internal sealed class EmailWorld : IAsyncDisposable
{
    private readonly ServiceProvider _provider;

    public EmailWorld(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<NotificationCatalog>();
        services.AddEmailChannel().AddAccountNotifications().AddMarketingNotifications();
        services.AddSingleton<IEmailSender>(Sender);
        services.AddSingleton<IEmailUnsubscribeLinks>(new FakeUnsubscribeLinks());
        configure?.Invoke(services);
        _provider = services.BuildServiceProvider();
    }

    public RecordingSender Sender { get; } = new();

    public NotificationCatalog Catalog => _provider.GetRequiredService<NotificationCatalog>();

    public EmailChannel Channel => _provider.GetServices<INotificationChannel>().OfType<EmailChannel>().Single();

    public EmailComposer Composer => _provider.GetRequiredService<EmailComposer>();

    public IEnumerable<IHostedService> Checks => _provider.GetServices<IHostedService>();

    public NotificationKind Kind(string name) => Catalog.TryGet(name, out var kind) ? kind : throw new KeyNotFoundException(name);

    /// <summary>The delivery of <paramref name="kind"/>'s sample in <paramref name="language"/>, as the pipeline would hand it over.</summary>
    public DispatchItem ItemOf(string kind, string? language = null, object? payload = null)
    {
        var notification = Kind(kind);
        var body = payload ?? notification.Sample;
        var contact = (ContactRecipient)notification.RecipientOf(body);
        if (language is not null)
        {
            contact = new ContactRecipient(contact.Channel, contact.Address, LanguageTag.Create(language).Value);
        }

        return new DispatchItem(Guid.Parse("018f0000-0000-7000-8000-000000000001"), notification, body, contact,
            RecipientKey.Of(new ReferenceRecipient("account", "42")), "idem-1");
    }

    public ValueTask DisposeAsync() => _provider.DisposeAsync();
}
