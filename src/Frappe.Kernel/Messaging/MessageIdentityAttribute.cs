namespace Frappe.Kernel.Messaging;

/// <summary>
/// The stable name a persisted message is stored under; it never changes once a message was stored, so the
/// class can be renamed or moved without stranding queued, scheduled or dead-lettered messages.
/// </summary>
/// <param name="name">The identity, such as <c>order.close_tab</c>; unique across every message.</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class MessageIdentityAttribute(string name) : Attribute
{
    /// <summary>Gets the stable identity.</summary>
    public string Name { get; } = name;
}
