namespace Frappe.Notifications.Channels.Email;

/// <summary>
/// The provider refused the credentials (a 401 or 403): the API key is missing, revoked or not allowed to send from this domain. It is a
/// configuration fault to alert on, never a verdict on the recipient, so the delivery is not recorded as failed: the message is redelivered
/// and, until the key is fixed, dead-lettered where an operator sees it.
/// </summary>
public sealed class EmailProviderConfigurationException : Exception
{
    public EmailProviderConfigurationException()
    {
    }

    public EmailProviderConfigurationException(string message)
        : base(message)
    {
    }

    public EmailProviderConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
