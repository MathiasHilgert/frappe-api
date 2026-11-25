using Frappe.Notifications.Application.Messages;

namespace Frappe.Notifications.Messages.Account;

/// <summary>
/// What <see cref="EmailChanged"/> needs: the replaced address (the one that gets the notice), its language, the person's name and the undo link.
/// The address, the name and the link (it carries a secret token) are private: <see cref="ToString"/> prints none (S13, EVT-20).
/// </summary>
/// <param name="Email">The replaced address, the one the notice goes to.</param>
/// <param name="Language">The recipient's language tag (<c>es-AR</c>); empty or unknown gets the default.</param>
/// <param name="Name">What to call the person: at most 60 characters and nothing that looks like a link.</param>
/// <param name="UndoUrl">The console page that restores the address, with the single-use token: an absolute https URL.</param>
public sealed record EmailChangedPayload(string Email, string? Language, string Name, string UndoUrl) : IPayloadRules
{
    /// <inheritdoc />
    public IReadOnlyList<string> InvalidFields()
    {
        List<string> invalid = [];
        if (!PayloadText.IsPersonName(Name))
        {
            invalid.Add("name");
        }

        if (!Uri.TryCreate(UndoUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            invalid.Add("undo_url");
        }

        return invalid;
    }

    public override string ToString() => "EmailChangedPayload { Email = ***, Language = " + Language + ", Name = ***, UndoUrl = *** }";
}
