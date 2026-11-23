using NodaTime;

namespace Frappe.Messaging.Relay;

/// <summary>An integration event leaving the module, with its CloudEvents attributes.</summary>
public sealed record OutgoingMessage(
    Guid Id,
    string Type,
    string Source,
    string DataContentType,
    ReadOnlyMemory<byte> Data,
    Instant Time,
    string? TraceParent,
    string? TraceState);
