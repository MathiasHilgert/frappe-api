using System.Net;

namespace Frappe.Notification.Application.Mails;

/// <summary>The provider refused the mail itself (bad address, bad key, bad payload): retrying cannot succeed.</summary>
public sealed class MailRejectedException(HttpStatusCode statusCode)
    : Exception($"The mail provider rejected the mail with status {(int)statusCode}.")
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}
