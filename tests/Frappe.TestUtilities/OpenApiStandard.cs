using System.Text.Json;

namespace Frappe.TestUtilities;

/// <summary>The description standard of the OpenAPI document: every operation has a summary and every schema property a description.</summary>
public static class OpenApiStandard
{
    /// <summary>Lists every operation without a summary and every schema property without a description.</summary>
    /// <param name="document">The OpenAPI document.</param>
    /// <returns>The violations; empty when the document is fully described.</returns>
    public static IReadOnlyList<string> Violations(JsonElement document)
    {
        List<string> violations = [];
        AddOperationViolations(document, violations);
        AddPropertyViolations(document, violations);
        return violations;
    }

    private static void AddOperationViolations(JsonElement document, ICollection<string> violations)
    {
        if (document.TryGetProperty("paths", out JsonElement paths))
        {
            foreach (JsonProperty path in paths.EnumerateObject())
            {
                foreach (JsonProperty operation in path.Value.EnumerateObject())
                {
                    if (!HasText(operation.Value, "summary"))
                    {
                        violations.Add(
                            $"{operation.Name.ToUpperInvariant()} {path.Name}: no summary"
                        );
                    }
                }
            }
        }
    }

    private static void AddPropertyViolations(JsonElement document, ICollection<string> violations)
    {
        if (
            document.TryGetProperty("components", out JsonElement components)
            && components.TryGetProperty("schemas", out JsonElement schemas)
        )
        {
            foreach (JsonProperty schema in schemas.EnumerateObject())
            {
                if (!schema.Value.TryGetProperty("properties", out JsonElement properties))
                {
                    continue;
                }

                foreach (JsonProperty property in properties.EnumerateObject())
                {
                    if (!HasText(property.Value, "description"))
                    {
                        violations.Add($"{schema.Name}.{property.Name}: no description");
                    }
                }
            }
        }
    }

    private static bool HasText(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement text)
        && text.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(text.GetString());
}
