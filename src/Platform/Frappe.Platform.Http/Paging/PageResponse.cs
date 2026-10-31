namespace Frappe.Platform.Http.Paging;

/// <summary>One page of a list.</summary>
/// <typeparam name="T">The item type.</typeparam>
/// <param name="Items">The items of this page.</param>
/// <param name="NextCursor">The cursor of the next page, or <see langword="null"/> on the last page.</param>
public sealed record PageResponse<T>(IReadOnlyList<T> Items, string? NextCursor);
