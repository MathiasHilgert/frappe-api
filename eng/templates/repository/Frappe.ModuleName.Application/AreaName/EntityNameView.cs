using NodaTime;

namespace Frappe.ModuleName.Application.AreaName;

/// <summary>
/// An EntityName as queries answer it. <paramref name="Version"/> is its row version (<c>xmin</c>), what an <c>If-Match</c> is compared with.
/// FILL IN: add the fields a caller reads.
/// </summary>
public sealed record EntityNameView(Guid Id, Instant CreatedAt, string Version);
