namespace Frappe.Notification.Domain;

/// <summary>
/// One email to one recipient: a plain-text part always, and optionally an HTML part with the images it shows inline
/// (referenced as <c>cid:&lt;content id&gt;</c>).
/// </summary>
public sealed record Mail
{
    public Mail(string to, string subject, string text, string? html = null, IReadOnlyList<InlineImage>? inlineImages = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(to);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        To = to;
        Subject = subject;
        Text = text;
        Html = html;
        InlineImages = inlineImages ?? [];
    }

    public string To { get; }

    public string Subject { get; }

    public string Text { get; }

    public string? Html { get; }

    public IReadOnlyList<InlineImage> InlineImages { get; }
}
