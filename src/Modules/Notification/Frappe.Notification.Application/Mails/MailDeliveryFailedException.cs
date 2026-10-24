using System.Net;

namespace Frappe.Notification.Application.Mails;

/// <summary>The provider could not take the mail right now (throttling, server error, timeout, network): retry later.</summary>
public sealed class MailDeliveryFailedException(string message, Exception? innerException, HttpStatusCode? statusCode = null)
    : Exception(message, innerException)
{
    public HttpStatusCode? StatusCode { get; } = statusCode;
}
