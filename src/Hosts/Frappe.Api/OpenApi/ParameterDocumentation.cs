using System.Globalization;
using System.Reflection;
using System.Text.Json.Nodes;
using Frappe.Http;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Frappe.Api.OpenApi;

/// <summary>
/// Types the query parameters an endpoint documents with <see cref="DocumentedParameterAttribute"/>: they bind as text
/// so the use case answers its own errors, and the generator would otherwise describe every one as an optional string.
/// </summary>
internal sealed class ParameterDocumentation : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        if (operation.Parameters is not { Count: > 0 } parameters)
        {
            return Task.CompletedTask;
        }

        foreach (var description in context.Description.ParameterDescriptions)
        {
            if (description.ParameterDescriptor is not IParameterInfoParameterDescriptor { ParameterInfo: { } info }
                || info.GetCustomAttribute<DocumentedParameterAttribute>() is not { } documented
                || parameters.OfType<OpenApiParameter>().FirstOrDefault(p => p.In == ParameterLocation.Query && p.Name == description.Name) is not { } parameter)
            {
                continue;
            }

            parameter.Required = documented.Required;
            parameter.Schema = Schema(documented);
        }

        return Task.CompletedTask;
    }

    private static OpenApiSchema Schema(DocumentedParameterAttribute documented)
    {
        var schema = documented.Format switch
        {
            ParameterFormat.WholeNumber => new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int32" },
            ParameterFormat.Date => new OpenApiSchema { Type = JsonSchemaType.String, Format = "date" },
            ParameterFormat.Uuid => new OpenApiSchema { Type = JsonSchemaType.String, Format = "uuid" },
            _ => new OpenApiSchema { Type = JsonSchemaType.String },
        };
        if (documented.Values is { Length: > 0 } values)
        {
            schema.Enum = [.. values.Select(value => (JsonNode)JsonValue.Create(value))];
        }

        schema.Pattern = documented.Pattern;
        if (documented.Minimum != DocumentedParameterAttribute.Unbounded)
        {
            schema.Minimum = documented.Minimum.ToString(CultureInfo.InvariantCulture);
        }

        if (documented.Maximum != DocumentedParameterAttribute.Unbounded)
        {
            schema.Maximum = documented.Maximum.ToString(CultureInfo.InvariantCulture);
        }

        return schema;
    }
}
