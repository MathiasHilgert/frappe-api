using System.Collections.Frozen;
using Frappe.Geo.Contracts.ValueObjects;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Frappe.Geo.Infrastructure.OpenApi;

/// <summary>
/// Describes geo's published value objects as the primitives their converters write, which the generator cannot see:
/// the codes, the street line, the time zone and the locale as strings (with their pattern where it is fixed), the minor unit as an
/// integer from 0 to 4.
/// </summary>
internal sealed class GeoSchemaTransformer : IOpenApiSchemaTransformer
{
    private static readonly FrozenDictionary<Type, string?> _strings = new Dictionary<Type, string?>
    {
        [typeof(CountryCode)] = "^[A-Z]{2}$",
        [typeof(CurrencyCode)] = "^[A-Z]{3}$",
        [typeof(SubdivisionCode)] = "^[A-Z]{2}-[A-Z0-9]{1,3}$",
        [typeof(PostalCode)] = null,
        [typeof(StreetLine)] = null,
        [typeof(TimeZoneId)] = null,
        [typeof(Locale)] = null,
    }.ToFrozenDictionary();

    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(context);
        Type type =
            Nullable.GetUnderlyingType(context.JsonTypeInfo.Type) ?? context.JsonTypeInfo.Type;
        if (_strings.TryGetValue(type, out string? pattern))
        {
            Reset(schema, JsonSchemaType.String);
            schema.Pattern = pattern;
        }
        else if (type == typeof(MinorUnit))
        {
            Reset(schema, JsonSchemaType.Integer);
            schema.Minimum = "0";
            schema.Maximum = "4";
        }

        return Task.CompletedTask;
    }

    // Replaces the object shape the generator inferred, keeping a nullable member nullable.
    private static void Reset(OpenApiSchema schema, JsonSchemaType type)
    {
        schema.Type =
            schema.Type is { } current && current.HasFlag(JsonSchemaType.Null)
                ? type | JsonSchemaType.Null
                : type;
        schema.Properties?.Clear();
        schema.Required?.Clear();
        schema.Format = null;
        schema.Pattern = null;
    }
}
