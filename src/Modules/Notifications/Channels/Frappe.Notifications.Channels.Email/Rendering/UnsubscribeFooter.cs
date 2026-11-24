namespace Frappe.Notifications.Channels.Email.Rendering;

/// <summary>The visible unsubscribe link of a Marketing mail (S9): a sentence and a link that points to the page of S5.</summary>
/// <param name="Text">Why the recipient gets the mail.</param>
/// <param name="LinkLabel">What the link says.</param>
/// <param name="Url">Where it goes.</param>
public sealed record UnsubscribeFooter(string Text, string LinkLabel, Uri Url)
{
    public override string ToString() => "UnsubscribeFooter { Text = ***, LinkLabel = ***, Url = *** }";
}
