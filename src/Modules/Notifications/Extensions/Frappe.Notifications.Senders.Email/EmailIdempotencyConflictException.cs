namespace Frappe.Notifications.Senders.Email;

/// <summary>
/// Resend answered 409 <c>invalid_idempotent_request</c>: the same idempotency key arrived with another body, which the deterministic
/// rendering should make impossible. It is a bug to alert on, never a verdict on the recipient, so the delivery is not recorded as failed:
/// the message is redelivered and, if it persists, dead-lettered where an operator sees it.
/// </summary>
public sealed class EmailIdempotencyConflictException : Exception
{
    public EmailIdempotencyConflictException()
    {
    }

    public EmailIdempotencyConflictException(string message)
        : base(message)
    {
    }

    public EmailIdempotencyConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
