using Frappe.Localization;

namespace Frappe.Notifications.Domain.Deliveries;

/// <summary>
/// Who a notification is for (S3): a <see cref="ReferenceRecipient"/> a contact directory resolves at send time, or a
/// <see cref="ContactRecipient"/> whose address is itself the subject of the fact. Nothing in it is shaped like one channel (S0).
/// </summary>
public closed record Recipient
{
    private protected Recipient()
    {
    }

    /// <summary>A person or thing the directory of <paramref name="kind"/> knows by <paramref name="id"/>.</summary>
    /// <exception cref="ArgumentException">The kind is not lower snake case or the id is blank: a bug of the notification's author.</exception>
    public static Recipient Reference(string kind, string id) => new ReferenceRecipient(kind, id);

    /// <summary>An explicit address over <paramref name="channel"/>, allowed only when the address is the subject of the fact.</summary>
    /// <exception cref="ArgumentException">The address is blank: a bug of the notification's author.</exception>
    public static Recipient Contact(ChannelName channel, string address, LanguageTag? language = null) =>
        new ContactRecipient(channel, address, language);
}
