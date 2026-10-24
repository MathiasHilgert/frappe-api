using System.Text.RegularExpressions;
using Frappe.Notification.Application.Mails;
using Frappe.Notification.Domain;
using Microsoft.Extensions.Logging;

namespace Frappe.Notification.Infrastructure.Mails;

/// <summary>
/// Development and test stand-in for a provider: logs that a mail was sent, never what it says. The recipient is reduced
/// to its domain and digit runs in the subject (one-time codes) are masked; the body is not logged at all.
/// </summary>
internal sealed partial class LoggingMailer(ILogger<LoggingMailer> logger) : IMailer
{
    public Task SendAsync(Mail mail, string idempotencyKey, CancellationToken cancellationToken)
    {
        if (logger.IsEnabled(LogLevel.Information))
        {
            var recipientDomain = RecipientDomain(mail.To);
            var subject = Digits().Replace(mail.Subject, "***");
            var kind = Kind(mail);
            LogMail(logger, recipientDomain, kind, subject);
        }
        return Task.CompletedTask;
    }

    private static string RecipientDomain(string to)
    {
        var at = to.LastIndexOf('@');
        return at < 0 ? "*" : "*" + to[at..];
    }

    private static string Kind(Mail mail) => mail.Html is null ? "text" : "text+html";

    [GeneratedRegex(@"\d+", RegexOptions.None, matchTimeoutMilliseconds: 100)]
    private static partial Regex Digits();

    [LoggerMessage(Level = LogLevel.Information, Message = "Mail to {RecipientDomain} ({Kind}): {Subject}")]
    private static partial void LogMail(ILogger logger, string recipientDomain, string kind, string subject);
}
