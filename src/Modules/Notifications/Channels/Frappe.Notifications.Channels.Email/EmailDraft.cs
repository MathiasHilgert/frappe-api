namespace Frappe.Notifications.Channels.Email;

/// <summary>
/// The content of one email as a notification kind composes it: a subject, a preheader and closed blocks. It is only data; the renderers
/// turn it into HTML and text. Subclasses add the builder methods their category allows, so what is not allowed does not compile (S7).
/// </summary>
public abstract class EmailDraft
{
    private readonly List<EmailBlock> _blocks = [];

    private protected EmailDraft(string subject)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        if (subject.AsSpan().ContainsAny('\r', '\n'))
        {
            throw new ArgumentException("A subject is one line.", nameof(subject));
        }

        Subject = subject.Trim();
    }

    /// <summary>The subject line; it may carry the verification code (S7).</summary>
    public string Subject { get; }

    /// <summary>The short text mail clients show next to the subject; empty when none.</summary>
    public string PreheaderText { get; private set; } = string.Empty;

    /// <summary>The blocks, in the order they are shown.</summary>
    public IReadOnlyList<EmailBlock> Blocks => _blocks;

    /// <summary>Whether the draft carries a link: a Security notification must not.</summary>
    public bool HasAction => _blocks.Exists(block => block is EmailBlock.Action);

    public override string ToString() => $"{GetType().Name} {{ Subject = ***, Blocks = {_blocks.Count} }}";

    private protected void SetPreheader(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        PreheaderText = text.Trim();
    }

    private protected void Add(EmailBlock block) => _blocks.Add(block);
}
