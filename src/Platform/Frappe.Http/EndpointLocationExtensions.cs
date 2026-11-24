using Microsoft.AspNetCore.Builder;

namespace Frappe.Http;

/// <summary>Endpoint conventions about the <c>Location</c> header of creation and acceptance answers.</summary>
public static class EndpointLocationExtensions
{
    extension<TBuilder>(TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        /// <summary>
        /// Declares that this endpoint's <c>201</c> or <c>202</c> answer has no addressable resource, so the OpenAPI document
        /// does not require a <c>Location</c> header for it (HTTP-09).
        /// </summary>
        public TBuilder WithoutLocation()
        {
            ArgumentNullException.ThrowIfNull(builder);
            return builder.WithMetadata(new WithoutLocationMetadata());
        }
    }
}
