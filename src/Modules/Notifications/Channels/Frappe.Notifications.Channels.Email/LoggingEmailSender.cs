using Frappe.Notifications.Application.Channels;
using Frappe.Notifications.Domain.Deliveries;
using Microsoft.Extensions.Logging;

namespace Frappe.Notifications.Channels.Email;

/// <summary>
/// Sends nothing: it records that an email would have gone out and answers that the provider took it, so development and tests run the
/// whole pipeline with no account. It logs only the delivery's idempotency key and language, never the address, the subject or the body
/// (S13). Its provider message id is derived from the key, so a redelivery answers the same id.
/// </summary>
public sealed partial class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    private const int IdLength = 16;

    public Task<ChannelOutcome> SendAsync(RenderedEmail email, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(email);
        var id = "logged-" + email.IdempotencyKey[..Math.Min(IdLength, email.IdempotencyKey.Length)];
        LogSent(logger, id, email.Language.Value);
        return Task.FromResult<ChannelOutcome>(new ChannelOutcome.Sent(DeliveryProvider.Resend, id, null));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Email {MessageId} was not sent: the logging sender is selected (language {Language})")]
    private static partial void LogSent(ILogger logger, string messageId, string language);
}
