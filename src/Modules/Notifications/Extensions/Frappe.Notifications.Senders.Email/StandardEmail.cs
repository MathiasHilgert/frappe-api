namespace Frappe.Notifications.Senders.Email;

/// <summary>The content of an email that may carry a link (Transactional and Marketing).</summary>
public sealed class StandardEmail : EmailDraft<StandardEmail>
{
    internal StandardEmail(string subject)
        : base(subject)
    {
    }

    /// <summary>Adds a link.</summary>
    /// <exception cref="ArgumentException">The label is blank or the url is not absolute https.</exception>
    public StandardEmail Action(string label, string url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("An action goes to an absolute https url.", nameof(url));
        }

        return Append(new EmailBlock.Action(label, uri));
    }
}
