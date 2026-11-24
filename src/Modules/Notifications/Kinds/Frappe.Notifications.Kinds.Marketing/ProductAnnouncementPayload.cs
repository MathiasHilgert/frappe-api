namespace Frappe.Notifications.Kinds.Marketing;

/// <summary>
/// What <see cref="ProductAnnouncement"/> needs: who to write to, their language and name, the headline and summary of the news and where
/// to read more. The address and name are personal data: <see cref="ToString"/> prints neither (S13, EVT-20).
/// </summary>
/// <param name="Email">The address of a recipient with no account (S3).</param>
/// <param name="Language">The recipient's language tag (<c>es-AR</c>); empty or unknown gets the default.</param>
/// <param name="Name">Kept in the producer contract for a personal greeting; the mail does not show it today.</param>
/// <param name="Headline">The title of the news, also in the subject.</param>
/// <param name="Summary">One paragraph about it.</param>
/// <param name="Url">The absolute https page that tells more.</param>
public sealed record ProductAnnouncementPayload(string Email, string? Language, string Name, string Headline, string Summary, string Url)
{
    public override string ToString() =>
        "ProductAnnouncementPayload { Email = ***, Language = " + Language + ", Name = ***, Headline = " + Headline + " }";
}
