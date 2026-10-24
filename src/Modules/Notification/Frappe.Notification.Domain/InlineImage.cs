namespace Frappe.Notification.Domain;

/// <summary>An image attached to a mail and shown inside its HTML part as <c>cid:<paramref name="ContentId"/></c>.</summary>
public sealed record InlineImage(string ContentId, string FileName, string ContentType, ReadOnlyMemory<byte> Content);
