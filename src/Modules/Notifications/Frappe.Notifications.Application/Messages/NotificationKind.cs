using Frappe.Notifications.Domain.Deliveries;

namespace Frappe.Notifications.Application.Messages;

/// <summary>
/// A notification kind as the pipeline sees it, without its payload type: the name, the category, the notification object a channel asks
/// for its content, and how to read the recipient out of an untyped payload.
/// </summary>
public sealed class NotificationKind
{
    private readonly Func<object, Recipient> _recipientOf;

    private NotificationKind(
        string name, NotificationCategory category, Type payloadType, object notification, object sample, Func<object, Recipient> recipientOf)
    {
        Name = name;
        Category = category;
        PayloadType = payloadType;
        Notification = notification;
        Sample = sample;
        _recipientOf = recipientOf;
    }

    public string Name { get; }

    public NotificationCategory Category { get; }

    public Type PayloadType { get; }

    /// <summary>The notification itself, which a channel casts to the interface it renders from.</summary>
    public object Notification { get; }

    /// <summary>The kind's sample payload.</summary>
    public object Sample { get; }

    /// <summary>Describes <typeparamref name="TNotification"/> as a kind.</summary>
    public static NotificationKind Of<TNotification, TPayload>()
        where TNotification : INotification<TPayload>, new()
        where TPayload : notnull
    {
        var notification = new TNotification();
        return new NotificationKind(
            TNotification.Kind, TNotification.Category, typeof(TPayload), notification, TNotification.Sample,
            payload => notification.RecipientOf((TPayload)payload));
    }

    /// <summary>Who the payload's notification is for.</summary>
    /// <exception cref="ArgumentException">The payload is not of <see cref="PayloadType"/>.</exception>
    public Recipient RecipientOf(object payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (!PayloadType.IsInstanceOfType(payload))
        {
            throw new ArgumentException($"Kind '{Name}' takes a {PayloadType.Name} payload.", nameof(payload));
        }

        return _recipientOf(payload);
    }
}
