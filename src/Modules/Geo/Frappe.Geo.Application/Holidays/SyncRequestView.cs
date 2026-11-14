using NodaTime;

namespace Frappe.Geo.Application.Holidays;

/// <summary>A run requested by hand, waiting for the Worker to start it.</summary>
public sealed record SyncRequestView(Guid Id, string SourceId, Instant RequestedAt);
