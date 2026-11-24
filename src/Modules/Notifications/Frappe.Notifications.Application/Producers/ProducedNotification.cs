using Frappe.Notifications.Application.Kinds;

namespace Frappe.Notifications.Application.Producers;

/// <summary>One notification a producer's mapper asks for: a kind by name and its payload. The payload never prints (S13).</summary>
/// <param name="Kind">The kind's name, as the catalog knows it.</param>
/// <param name="Payload">The payload, of the kind's payload type.</param>
public sealed record ProducedNotification(string Kind, object Payload)
{
    /// <summary>Asks for <typeparamref name="TNotification"/> with <paramref name="payload"/>, so the kind's name is never retyped.</summary>
    public static ProducedNotification Of<TNotification, TPayload>(TPayload payload)
        where TNotification : INotification<TPayload>
        where TPayload : notnull
    {
        ArgumentNullException.ThrowIfNull(payload);
        return new ProducedNotification(TNotification.Kind, payload);
    }

    public override string ToString() => $"ProducedNotification {{ Kind = {Kind}, Payload = *** }}";
}
