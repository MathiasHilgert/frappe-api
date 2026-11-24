using System.Text.Json.Serialization;

namespace Frappe.Identity.Http.Sessions;

/// <summary>One page of the caller's live sessions, newest opened first (HTTP-14, HTTP-15).</summary>
/// <param name="Sessions">The sessions of the page.</param>
/// <param name="NextCursor">The <c>cursor</c> that asks for the next page; absent on the last page.</param>
public sealed record MySessionListResponse(
    IReadOnlyList<MySessionResponse> Sessions,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? NextCursor);
