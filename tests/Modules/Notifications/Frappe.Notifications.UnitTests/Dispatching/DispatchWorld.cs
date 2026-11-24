using Frappe.Localization;
using Frappe.Notifications.Application.Contacts;
using Frappe.Notifications.Application.Dispatching;
using Frappe.Notifications.Application.Kinds;
using Frappe.Notifications.Domain.Deliveries;
using Frappe.Notifications.Infrastructure.Addresses;
using Frappe.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Frappe.Notifications.UnitTests.Dispatching;

/// <summary>The dispatcher wired over fakes, with the real hasher and metrics: what each test arranges, runs and reads.</summary>
internal sealed class DispatchWorld : IDisposable
{
    public static readonly Guid MessageId = Guid.Parse("0192b6f0-0000-7000-8000-000000000001");

    private readonly CapturedLogs _logs = new();
    private readonly ServiceProvider _meters = new ServiceCollection().AddMetrics().BuildServiceProvider();

    public DispatchWorld(params IContactResolver[] resolvers)
    {
        Mail = new FakeChannel(TestChannels.Mail);
        Hasher = TestHasher.Create();
        Capture = new MetricCapture(_meters.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>());
        Pending = new PendingDeliveries(new NotificationsMetrics(_meters.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>()));
        Dispatcher = new NotificationDispatcher(
            new RecipientResolution(new ContactDirectory(resolvers), Hasher),
            new PreferenceGate(Preferences, Hasher),
            new ChannelSelection([Mail, Text]),
            new SuppressionGate(Suppressions, Hasher),
            new DeliveryRecorder(Staging, new FakeTimeProvider(new DateTimeOffset(2026, 11, 24, 10, 0, 0, TimeSpan.Zero))),
            Pending,
            _logs.For<NotificationDispatcher>());
    }

    public FakeChannel Mail { get; }

    public FakeChannel Text { get; } = new(TestChannels.Text) { Accepts = false };

    public HmacAddressHasher Hasher { get; }

    public FakePreferences Preferences { get; } = new();

    public FakeSuppressions Suppressions { get; } = new();

    public FakeStaging Staging { get; } = new();

    public MetricCapture Capture { get; }

    public PendingDeliveries Pending { get; }

    public CapturedLogs Logs => _logs;

    public NotificationDispatcher Dispatcher { get; }

    public static NotificationKind Security { get; } = NotificationKind.Of<TestSecurityNotification, TestPayload>();

    public static NotificationKind Marketing { get; } = NotificationKind.Of<TestMarketingNotification, TestPayload>();

    public static ContactRecipient Contact(ChannelName channel, string address) => new(channel, address, LanguageTag.Create("es").Value);

    public Task<IReadOnlyList<DispatchOutcome>> DispatchAsync(NotificationKind kind, TestPayload payload) =>
        Dispatcher.DispatchAsync(new NotificationRequest(MessageId, kind, payload), CancellationToken.None);

    public void Dispose()
    {
        Capture.Dispose();
        _logs.Dispose();
        _meters.Dispose();
    }
}
