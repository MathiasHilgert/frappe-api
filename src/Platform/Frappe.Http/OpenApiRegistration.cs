using Frappe.Http.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Frappe.Http;

/// <summary>The OpenAPI document of the API and the documentation every endpoint convention contributes to it.</summary>
public static class OpenApiRegistration
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers the document <paramref name="documentName"/> with the platform's transformers: the Problem Details every
        /// error answers, the caching, language, idempotency, <c>Location</c> and <c>Origin</c> check headers, the typed
        /// query parameters and the string enumerations. Its <c>info</c> is bound from <c>Http:OpenApi</c>
        /// (<see cref="ApiDocumentOptions"/>), validated at startup.
        /// </summary>
        public IServiceCollection AddFrappeOpenApi(string documentName = "v1")
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentException.ThrowIfNullOrWhiteSpace(documentName);
            services.AddOptions<ApiDocumentOptions>().BindConfiguration(ApiDocumentOptions.Section).ValidateOnStart();
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<ApiDocumentOptions>, ApiDocumentOptionsValidator>());
            return services.AddOpenApi(documentName, options => options
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
}
