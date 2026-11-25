using System.Text;

namespace Frappe.Notifications.Senders.Email.Rendering;

/// <summary>Renders an <see cref="EmailDocument"/> to <c>text/plain</c> from the same blocks as the HTML (S7): same words, same order.</summary>
public static class PlainTextEmailRenderer
{
    public static string Render(EmailDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var text = new StringBuilder();
        foreach (var block in document.Blocks)
        {
            text.Append(block switch
            {
                EmailBlock.Heading heading => heading.Text,
                EmailBlock.Paragraph paragraph => paragraph.Text,
                EmailBlock.Code code => code.Value,
                EmailBlock.Meta meta => string.Join(" · ", meta.Items),
                EmailBlock.Footer footer => $"--\n{footer.Text}",
                EmailBlock.Action action => $"{action.Label}: {action.Url.AbsoluteUri}",
                _ => throw new InvalidOperationException($"Unknown email block {block.GetType().Name}."),
            }).Append("\n\n");
        }

        if (document.Unsubscribe is { } unsubscribe)
        {
            text.Append("--\n").Append(unsubscribe.Text).Append('\n').Append(unsubscribe.LinkLabel).Append(": ")
                .Append(unsubscribe.Url.AbsoluteUri).Append('\n');
        }

        return text.ToString().TrimEnd('\n') + "\n";
    }
}
