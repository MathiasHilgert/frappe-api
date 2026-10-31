using System.Text.Json.Serialization;

namespace Frappe.Platform.Http.Problems;

/// <summary>One entry of a problem's <c>errors</c> array.</summary>
/// <param name="Code">The stable error code clients branch on, such as <c>kernel.entity_id.invalid</c>.</param>
/// <param name="JsonPointer">The JSON Pointer of the failing input, or <see langword="null"/> when no input is at fault.</param>
/// <param name="Detail">The text in the request's locale.</param>
/// <param name="Args">The values the text refers to, or <see langword="null"/> when it refers to none.</param>
public sealed record ProblemError(
    string Code,
    [property: JsonPropertyName("pointer")] string? JsonPointer,
    string Detail,
    IReadOnlyDictionary<string, object>? Args
);
