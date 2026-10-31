using System.Collections.Frozen;
using Frappe.Kernel.Currencies;
using Frappe.Kernel.Domain;
using Frappe.Kernel.Geography;
using Frappe.Kernel.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using NodaTime;

namespace Frappe.Platform.Http.OpenApi;

/// <summary>
/// Describes the types whose JSON form comes from a converter, which the generator cannot see: dates, typed ids and
/// the Kernel value objects, each as the primitive it is written as; enums get their string type and the house
/// problem its member descriptions.
/// </summary>
internal sealed class SchemaTransformers : IOpenApiSchemaTransformer
{
    // The types written as a JSON string, with the format or pattern of that string.
    private static readonly FrozenDictionary<Type, (string? Format, string? Pattern)> _strings =
        new Dictionary<Type, (string?, string?)>
        {
            [typeof(LocalDate)] = ("date", null),
            [typeof(CountryCode)] = (null, "^[A-Z]{2}$"),
            [typeof(CurrencyCode)] = (null, "^[A-Z]{3}$"),
            [typeof(SubdivisionCode)] = (null, "^[A-Z]{2}-[A-Z0-9]{1,3}$"),
            [typeof(PostalCode)] = (null, null),
            [typeof(TimeZoneId)] = (null, null),
            [typeof(Locale)] = (null, null),
        }.ToFrozenDictionary();

    private static readonly FrozenDictionary<string, string> _problemMembers = new Dictionary<
        string,
        string
    >(StringComparer.Ordinal)
    {
        ["type"] = "The stable URN of the problem category, such as urn:frappe:problem:validation.",
        ["title"] = "The fixed English title of the problem type.",
        ["status"] = "The HTTP status code.",
        ["detail"] = "Not used; each entry of errors carries its own localized detail.",
        ["instance"] = "The request path.",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken
    )
    {
        Type type =
            Nullable.GetUnderlyingType(context.JsonTypeInfo.Type) ?? context.JsonTypeInfo.Type;
        if (_strings.TryGetValue(type, out (string? Format, string? Pattern) text))
        {
            AsString(schema, text.Format, text.Pattern);
        }
        else if (type == typeof(MinorUnit))
        {
            Reset(schema, JsonSchemaType.Integer);
            schema.Minimum = "0";
            schema.Maximum = "4";
        }
        else if (type == typeof(Address))
        {
            AsAddress(schema);
        }
        else if (type.IsEnum)
        {
            // The generator lists the snake_case values but leaves out their type.
            schema.Type = KeepingNull(schema, JsonSchemaType.String);
        }
        else if (type == typeof(ProblemDetails))
        {
            Describe(schema);
        }
        else if (IsTypedId(type))
        {
            AsString(schema, "uuid", pattern: null);
        }

        return Task.CompletedTask;
    }

    private static bool IsTypedId(Type type) =>
        Array.Exists(
            type.GetInterfaces(),
            static contract =>
                contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IEntityId<>)
        );

    private static void AsAddress(OpenApiSchema schema)
    {
        Reset(schema, JsonSchemaType.Object);
        schema.Properties = new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal)
        {
            ["streetLine"] = new OpenApiSchema { Type = JsonSchemaType.String },
            ["cityId"] = new OpenApiSchema { Type = JsonSchemaType.String, Format = "uuid" },
            ["postalCode"] = new OpenApiSchema { Type = JsonSchemaType.String },
        };
        schema.Required = new HashSet<string>(StringComparer.Ordinal) { "streetLine", "cityId" };
    }

    // The framework type carries no XML docs, so the house problem members are described here.
    private static void Describe(OpenApiSchema problem)
    {
        foreach (
            (string name, IOpenApiSchema member) in problem.Properties
                ?? new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal)
        )
        {
            if (
                member is OpenApiSchema schema
                && _problemMembers.TryGetValue(name, out string? description)
            )
            {
                schema.Description = description;
            }
        }
    }

    private static void AsString(OpenApiSchema schema, string? format, string? pattern)
    {
        Reset(schema, JsonSchemaType.String);
        schema.Format = format;
        schema.Pattern = pattern;
    }

    private static JsonSchemaType KeepingNull(OpenApiSchema schema, JsonSchemaType type) =>
        schema.Type is { } current && current.HasFlag(JsonSchemaType.Null)
            ? type | JsonSchemaType.Null
            : type;

    // Keeps a nullable member nullable while replacing the object shape the generator inferred.
    private static void Reset(OpenApiSchema schema, JsonSchemaType type)
    {
        schema.Type = KeepingNull(schema, type);
        schema.Properties?.Clear();
        schema.Required?.Clear();
        schema.Format = null;
        schema.Pattern = null;
    }
}
