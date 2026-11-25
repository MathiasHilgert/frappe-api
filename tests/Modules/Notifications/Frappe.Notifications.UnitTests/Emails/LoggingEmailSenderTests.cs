using Frappe.Localization;
using Frappe.Notifications.Application.Senders;
using Frappe.Notifications.Domain.Deliveries;
using Frappe.Notifications.Senders.Email;
using Frappe.Testing;
using Microsoft.Extensions.Logging;

namespace Frappe.Notifications.UnitTests.Emails;

/// <summary>The development sender sends nothing and prints nothing a recipient could be identified or a code stolen from (S13).</summary>
public sealed class LoggingEmailSenderTests
{
    [Test]
    public async Task It_records_the_send_as_taken_without_logging_the_address_the_subject_or_the_body()
    {
        // Arrange
        using var logs = new CapturedLogs();
        var sender = new LoggingEmailSender(logs.For<LoggingEmailSender>());
        var email = new RenderedEmail(
            EmailAddress.Create("ana@example.com").Value, "Tu código 482913", "<p>482913</p>", "482913", new Dictionary<string, string>(),
            "0123456789abcdef0123456789abcdef", LanguageTag.Create("en").Value);

        // Act
        var first = await sender.SendAsync(email, default);
        var again = await sender.SendAsync(email, default);

        // Assert
        var logged = string.Join('\n', logs.Entries.Select(e => e.Message));
        using (Assert.Multiple())
        {
            await Assert.That(first).IsEqualTo(new ChannelOutcome.Sent(DeliveryProvider.Resend, "logged-0123456789abcdef", null));
            await Assert.That(again).IsEqualTo(first);
            await Assert.That(logs.Entries.All(e => e.Level == LogLevel.Information)).IsTrue();
            await Assert.That(logged).DoesNotContain("ana@example.com").And.DoesNotContain("482913").And.Contains("0123456789abcdef");
        }
    }
}
