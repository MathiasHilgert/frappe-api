using Frappe.Kernel.Messaging;

namespace Frappe.Geo.Application.Timezones.ListTimezones;

/// <summary>Handles <see cref="ListTimezonesQuery"/>.</summary>
/// <param name="timezones">The catalogue's read port.</param>
public sealed class ListTimezonesHandler(IListTimezonesQueryService timezones)
    : IQueryHandler<ListTimezonesQuery, IReadOnlyList<TimezoneDto>>
{
    /// <inheritdoc />
    public async Task<ErrorOr<IReadOnlyList<TimezoneDto>>> Handle(
        ListTimezonesQuery query,
        CancellationToken cancellationToken
    ) => (await timezones.ListAsync(cancellationToken)).ToErrorOr();
}
