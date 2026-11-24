namespace Frappe.Notifications.Channels.Email;

/// <summary>
/// The provider could not take the email now (a 429, a 5xx, a request timeout, the concurrent-idempotency conflict): nothing is recorded
/// and the message is redelivered, with the same idempotency key, until it is taken or dead-lettered (S14).
/// </summary>
public sealed class TransientEmailFailureException : Exception
{
    public TransientEmailFailureException()
    {
    }

    public TransientEmailFailureException(string message)
        : base(message)
    {
    }

    public TransientEmailFailureException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
