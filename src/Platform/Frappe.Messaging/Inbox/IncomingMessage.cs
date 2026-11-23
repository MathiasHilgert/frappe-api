namespace Frappe.Messaging.Inbox;

/// <summary>An integration event as it arrives, with its CloudEvents attributes.</summary>
public sealed record IncomingMessage(
    Guid Id,
    string Type,
    ReadOnlyMemory<byte> Data,
    string? TraceParent,
    string? TraceState);
