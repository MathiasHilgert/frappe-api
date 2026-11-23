namespace Frappe.Http;

/// <summary>
/// What a client asked of a paged list (HTTP-14): how many items (<see cref="Limit"/>, 1 to
/// <see cref="PageLimits.Max"/>) and where to start (<see cref="After"/>, the position the previous page ended at; none
/// for the first page). Both arrive as the client wrote them (<c>limit</c>, <c>cursor</c>) and are bound here, so every
/// list answers the same errors: <c>platform.page_limit.out_of_range</c>, <c>platform.page_limit.invalid</c> and
/// <c>platform.page_cursor.invalid</c>.
/// </summary>
/// <typeparam name="TPosition">The list's own notion of a position (its sort key); it travels inside the opaque cursor.</typeparam>
public sealed record PageRequest<TPosition>(int Limit, TPosition? After)
    where TPosition : class;
