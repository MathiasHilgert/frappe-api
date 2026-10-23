using Frappe.Notification.Application;
using Frappe.Notification.Domain;
using Microsoft.Extensions.Logging;

namespace Frappe.Notification.Infrastructure;

/// <summary>Development and test stand-in for a provider: the mail, code included, only goes to the log.</summary>
internal sealed partial class LoggingMailer(ILogger<LoggingMailer> logger) : IMailer
{
    public Task SendAsync(Mail mail, CancellationToken ct)
    {
        LogMail(logger, mail.To, mail.Subject, mail.Text);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Mail to {To}: {Subject}\n{Text}")]
    private static partial void LogMail(ILogger logger, string to, string subject, string text);
}
