using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Frappe.Platform.Http.OpenApi;

/// <summary>
/// Lifts the description of a nullable member to the member itself. The generator writes a nullable reference to a
/// component as <c>oneOf: [{ type: null }, { $ref, description }]</c> (or <c>anyOf</c>), so the XML-doc description
/// lands on the non-null branch and the member reads as undescribed. A document transformer, because each module's XML
/// schema transformers run after the platform's and only the document is complete once they all ran.
/// </summary>
internal sealed class NullableMemberDescriptions : IOpenApiDocumentTransformer
{
    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(document);
        foreach (IOpenApiSchema schema in document.Components?.Schemas?.Values ?? [])
        {
            foreach (IOpenApiSchema member in schema.Properties?.Values ?? [])
            {
                Lift(member);
            }
        }

        return Task.CompletedTask;
    }

    // Internal for its unit test: a member's schema as the generator writes it.
    internal static void Lift(IOpenApiSchema member)
    {
        if (member is not OpenApiSchema { Description: null or "" } nullable)
        {
            return;
        }

        IOpenApiSchema[] branches =
        [
            .. (nullable.OneOf ?? nullable.AnyOf ?? []).Where(static branch =>
                branch.Type != JsonSchemaType.Null
            ),
        ];
        if (branches is [{ Description: { Length: > 0 } description }])
        {
            nullable.Description = description;
        }
    }
}
