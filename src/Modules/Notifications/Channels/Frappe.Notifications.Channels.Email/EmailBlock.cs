namespace Frappe.Notifications.Channels.Email;

/// <summary>
/// One piece of an email, from a closed set (S7): a producer composes blocks and never writes markup, so the same blocks render the HTML
/// and the plain text. Every block prints no content, because a block may carry a code or a name (S13).
/// </summary>
public closed record EmailBlock
{
    private protected EmailBlock()
    {
    }

    /// <summary>The title of the mail.</summary>
    /// <param name="Text">The title.</param>
    public sealed record Heading(string Text) : EmailBlock
    {
        public override string ToString() => "Heading { Text = *** }";
    }

    /// <summary>A paragraph of running text.</summary>
    /// <param name="Text">The text.</param>
    public sealed record Paragraph(string Text) : EmailBlock
    {
        public override string ToString() => "Paragraph { Text = *** }";
    }

    /// <summary>A verification code, shown one character per box.</summary>
    /// <param name="Value">The code.</param>
    public sealed record Code(string Value) : EmailBlock
    {
        public override string ToString() => "Code { Value = *** }";
    }

    /// <summary>A row of short facts about the code or the message ("Vence en 15 minutos", "Un solo uso").</summary>
    /// <param name="Items">The facts, in order.</param>
    public sealed record Meta(IReadOnlyList<string> Items) : EmailBlock
    {
        public override string ToString() => "Meta { Items = *** }";
    }

    /// <summary>The closing line: why the recipient got the mail and what to do if it was not them.</summary>
    /// <param name="Text">The text.</param>
    public sealed record Footer(string Text) : EmailBlock
    {
        public override string ToString() => "Footer { Text = *** }";
    }

    /// <summary>A link to follow. Only <see cref="StandardEmail"/> can add one; Security mails carry none.</summary>
    /// <param name="Label">What the link says.</param>
    /// <param name="Url">Where it goes.</param>
    public sealed record Action(string Label, Uri Url) : EmailBlock
    {
        public override string ToString() => "Action { Label = ***, Url = *** }";
    }
}
