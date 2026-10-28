namespace Frappe.Kernel.Messaging;

/// <summary>
/// Marks a message that runs without a tenant, such as a global maintenance command; any other message
/// that arrives without a tenant fails before its handler runs.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class GlobalMessageAttribute : Attribute;
