namespace Frappe.Notifications.Domain.Deliveries;

/// <summary>
/// What a delivery is for: the message that asked, the kind, the recipient and the channel. These four make a delivery unique (S14), so
/// they are also how redelivery finds the row that already exists.
/// </summary>
/// <param name="SourceMessageId">The id of the message that asked for the notification.</param>
/// <param name="Kind">The notification kind.</param>
/// <param name="Category">The kind's category now, kept on the row as a snapshot.</param>
/// <param name="Channel">The channel the delivery goes over.</param>
/// <param name="RecipientKey">The recipient's key.</param>
/// <param name="AddressHash">The recipient's address hash, when an address is known.</param>
public sealed record DeliveryTarget(
    Guid SourceMessageId,
    string Kind,
    NotificationCategory Category,
    ChannelName Channel,
    RecipientKey RecipientKey,
    AddressHash? AddressHash);
