namespace Frappe.Geo.Infrastructure.Sync;

/// <summary>What one conditional download answered.</summary>
internal sealed record FetchResult(FetchStatus Status, ReadOnlyMemory<byte> Body = default, string? ETag = null, DateTimeOffset? LastModified = null, string? Sha256 = null);
