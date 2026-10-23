namespace Frappe.Notification.Domain;

/// <summary>One plain-text email to one recipient.</summary>
public sealed record Mail
{
    public Mail(string to, string subject, string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(to);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        To = to;
        Subject = subject;
        Text = text;
    }

    public string To { get; }

    public string Subject { get; }

    public string Text { get; }
}
