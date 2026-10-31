using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Scalar.AspNetCore;

namespace Frappe.Platform.Http.OpenApi;

/// <summary>The OpenAPI document and the Scalar UI, published only in Development.</summary>
internal static class OpenApiExtensions
{
    public static void AddFrappeOpenApi(IServiceCollection services) =>
        services.AddOpenApi(static options => options.AddSchemaTransformer<SchemaTransformers>());

    // The document is the API's attack map, so deployed environments never publish it.
    public static void MapFrappeOpenApi(WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
        {
            return;
        }

        app.MapOpenApi();
        app.MapScalarApiReference();
    }
}
