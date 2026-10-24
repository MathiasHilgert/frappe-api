using Microsoft.OpenApi;
using Scalar.AspNetCore;

namespace Frappe.Api;

internal static class OpenApiDocs
{
    public static IServiceCollection AddOpenApiDocs(this IServiceCollection services) =>
        services.AddOpenApi("v1", options => options.AddDocumentTransformer((document, _, _) =>
        {
            document.Info = new OpenApiInfo { Title = "Frappé API", Version = "v1" };
            return Task.CompletedTask;
        }));

    public static WebApplication MapOpenApiDocs(this WebApplication app)
    {
        // Development only: the API surface is not published in other environments.
        if (!app.Environment.IsDevelopment())
            return app;

        // The fallback policy requires authentication; docs must opt out explicitly.
        app.MapOpenApi().AllowAnonymous();
        app.MapScalarApiReference().AllowAnonymous();
        return app;
    }
}
