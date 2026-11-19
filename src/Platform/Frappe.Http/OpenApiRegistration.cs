using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Frappe.Http;

/// <summary>
/// Wires the API document into a host: the <c>v1</c> OpenAPI document with the transformers that describe what every
/// endpoint shares (problems, language, caching, idempotency, <c>Location</c>, typed query parameters, string enums), and
/// the <see cref="ApiDocumentOptions"/> its <c>info</c> reads.
/// </summary>
internal static class OpenApiRegistration
{
    extension(IServiceCollection services)
    {
        /// <summary>Registers the <see cref="DocumentName"/> document, its transformers and its validated, configuration-bound options.</summary>
        public IServiceCollection AddFrappeOpenApi()
        {
            services.AddOptions<ApiDocumentOptions>().BindConfiguration(ApiDocumentOptions.Section).ValidateOnStart();
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<ApiDocumentOptions>, ApiDocumentOptionsValidator>());
            return services.AddOpenApi(DocumentName, options => options
                .AddDocumentTransformer<ApiDocumentInfo>()
                .AddDocumentTransformer<ProblemDocumentation>()
                .AddDocumentTransformer<OriginCheckDocumentation>()
                .AddOperationTransformer<ProblemDocumentation>()
                .AddOperationTransformer<AcceptLanguageDocumentation>()
                .AddOperationTransformer<CachingDocumentation>()
                .AddOperationTransformer<ParameterDocumentation>()
                .AddOperationTransformer<LocationDocumentation>()
                .AddOperationTransformer<IdempotencyDocumentation>()
                .AddSchemaTransformer<StringEnumDocumentation>());
        }
    }

    /// <summary>The name of the document the host maps (<c>/openapi/v1.json</c>).</summary>
    public const string DocumentName = "v1";
}
