namespace Frappe.Messaging.Relay;

/// <summary>The broker refused this message (too large, invalid subject, rejected by the stream). Retrying the same bytes may never work.</summary>
public sealed class MessageRejectedException : Exception
{
    public MessageRejectedException()
    {
    }

    public MessageRejectedException(string message)
        : base(message)
    {
    }

    public MessageRejectedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
