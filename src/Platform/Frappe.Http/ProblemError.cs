using System.Text.Json.Serialization;

namespace Frappe.Http;

/// <summary>One entry of the <c>errors</c> extension of a Problem Details response.</summary>
public sealed record ProblemError(
    string Code,
    IReadOnlyDictionary<string, object?> Parameters,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Message);
