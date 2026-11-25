namespace Frappe.Notifications.Senders.Email;

/// <summary>
/// The blocks every kind of email can have. <typeparamref name="TSelf"/> keeps the chain on the builder's own type, so
/// <see cref="SecureEmail"/> never turns into something that has an action.
/// </summary>
/// <typeparam name="TSelf">The builder type itself.</typeparam>
public abstract class EmailDraft<TSelf> : EmailDraft
    where TSelf : EmailDraft<TSelf>
{
    private const int MaxCodeLength = 12;

    private protected EmailDraft(string subject)
        : base(subject)
    {
    }

    /// <summary>Sets the preheader.</summary>
    public TSelf Preheader(string text)
    {
        SetPreheader(text);
        return Self;
    }

    /// <summary>Adds the title.</summary>
    public TSelf Heading(string text) => Append(new EmailBlock.Heading(Required(text)));

    /// <summary>Adds a paragraph.</summary>
    public TSelf Paragraph(string text) => Append(new EmailBlock.Paragraph(Required(text)));

    /// <summary>Adds a verification code: one to twelve characters without spaces, kept as text so no number format touches it.</summary>
    /// <exception cref="ArgumentException">The code is empty, has a space or is longer than twelve characters.</exception>
    public TSelf Code(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        if (value.Length > MaxCodeLength || value.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException($"A code is 1 to {MaxCodeLength} characters without spaces.", nameof(value));
        }

        return Append(new EmailBlock.Code(value));
    }

    /// <summary>Adds a row of short facts.</summary>
    public TSelf Meta(params string[] items)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentOutOfRangeException.ThrowIfZero(items.Length);
        return Append(new EmailBlock.Meta([.. items.Select(Required)]));
    }

    /// <summary>Adds the closing line.</summary>
    public TSelf Footer(string text) => Append(new EmailBlock.Footer(Required(text)));

    private protected TSelf Append(EmailBlock block)
    {
        Add(block);
        return Self;
    }

    private TSelf Self => (TSelf)this;

    private static string Required(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return text;
    }
}
