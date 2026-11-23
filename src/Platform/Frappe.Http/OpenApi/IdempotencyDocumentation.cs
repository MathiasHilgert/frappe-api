using Frappe.UseCases;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Frappe.Http.OpenApi;

/// <summary>
/// Declares the <c>Idempotency-Key</c> request header of every operation that accepts one (<see cref="IdempotentMetadata"/>)
/// and the <c>Idempotent-Replayed</c> header its answers carry when they are a replay. The problems it adds (400, 409 and 422) are declared by the
/// endpoint convention itself; the 503 is every operation's (<c>ProblemDocumentation</c>).
/// </summary>
internal sealed class IdempotencyDocumentation : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        if (!context.Description.ActionDescriptor.EndpointMetadata.OfType<IdempotentMetadata>().Any())
        {
            return Task.CompletedTask;
        }

        operation.Parameters ??= [];
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = IdempotencyKey.HeaderName,
            In = ParameterLocation.Header,
            Required = false,
            Description = "Makes the request safe to retry: the first successful (2xx) answer is stored for 24 hours and replayed, with "
                + $"{IdempotencyKey.ReplayedHeaderName}: true, to every later request with the same key, caller and content, "
                + "without running it again. Send a new key (a UUID is fine) for each new request. 400 platform.idempotency_key.invalid "
                + $"for a key that is not 1 to {IdempotencyKey.MaxLength} printable characters without spaces or is sent twice; 422 "
                + "platform.idempotency_key.reused when the key was used for a different request; 409 platform.idempotency_key.in_progress "
                + "while the first request still runs; 503 platform.idempotency.unavailable when answers cannot be kept (the request was not run). "
                + "A refused or failed request is not stored, so it can be retried with the same key. Cookies are never replayed.",
            Schema = new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                MinLength = 1,
                MaxLength = IdempotencyKey.MaxLength,
                Pattern = "^[\\x21-\\x7E]+$"
            },
        });

        if (operation.Responses is not null && operation.Responses.TryGetValue("409",
            out var conflictResponse) && conflictResponse is OpenApiResponse conflict)
        {
            conflict.Description = $"Conflict: the first request with this Idempotency-Key is still running ({IdempotencyKey.InProgressCode}), or "
                + $"other writers kept changing the same data while the operation committed ({CommitConflict.Code}). "
                + "The same request may be sent again.";
        }

        foreach (var (status, response) in operation.Responses ?? [])
        {
            if (status.StartsWith('2') && response is OpenApiResponse success)
            {
                success.Headers ??= new Dictionary<string, IOpenApiHeader>();
                success.Headers[IdempotencyKey.ReplayedHeaderName] = new OpenApiHeader
                {
                    Description = "true when this is the stored answer of an earlier request with the same Idempotency-Key; absent on the first "
                        + "answer.",
                    Schema = new OpenApiSchema { Type = JsonSchemaType.String },
                };
            }
        }

        return Task.CompletedTask;
    }
}
