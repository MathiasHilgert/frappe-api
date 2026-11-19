using System.Globalization;
using System.Text.Json.Nodes;
using Frappe.UseCases;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Frappe.Http;

/// <summary>
/// Describes the Problem Details every error really answers (RFC 9457 with a category as <c>type</c>, plus the
/// <c>errors</c> and <c>traceId</c>) as the reusable <c>Problem</c> schema, references it from every 4xx and 5xx
/// response, and declares:
/// <list type="bullet">
/// <item><description><c>500</c>: any operation may answer it.</description></item>
/// <item><description><c>503</c>: any operation may answer it while a database does not (<see cref="HttpRegistration.DatabaseUnavailableCode"/>).</description></item>
/// <item><description><c>409</c>: any operation that commits may answer it (<see cref="ICommitRejection.CommitConflictCode"/>).</description></item>
/// <item><description><c>415</c>: any operation that reads a body may answer it.</description></item>
/// </list>
/// It also declares the headers of problems: <c>Content-Language</c> on all of them, <c>Retry-After</c> on <c>429</c>
/// and <c>503</c>, and <c>WWW-Authenticate</c> on <c>401</c>. Routes answer HEAD wherever they answer GET, but the
/// document keeps only the GET operation: HEAD is the same operation without its content (RFC 9110 §9.3.2).
/// </summary>
internal sealed class ProblemDocumentation : IOpenApiOperationTransformer, IOpenApiDocumentTransformer
{
    public const string ProblemSchema = "Problem";
    public const string ProblemErrorSchema = "ProblemError";
    private const string ProblemContentType = "application/problem+json";

    /// <summary>The extension that declares an enumeration open to new values (Zalando API guidelines, honoured by oasdiff).</summary>
    private const string ExtensibleEnum = "x-extensible-enum";

    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        var document = context.Document ?? throw new InvalidOperationException("The operation has no document to hold the Problem schema.");
        AddSchemas(document);

        operation.Responses ??= [];
        if (!operation.Responses.ContainsKey("500"))
        {
            operation.Responses["500"] = new OpenApiResponse { Description = "Internal Server Error" };
        }

        // Every operation reads a module's database, and a database that does not answer is a 503 on any route.
        if (!operation.Responses.ContainsKey("503"))
        {
            operation.Responses["503"] = new OpenApiResponse
            {
                Description = $"Service Unavailable: a database the operation needs is not answering ({HttpRegistration.DatabaseUnavailableCode}); the same request may succeed after Retry-After.",
            };
        }

        // A commit that other writers keep moving rows under is a 409 the client may retry (the unit of work gives up after its repairs).
        if (Commits(context) && !operation.Responses.ContainsKey("409"))
        {
            operation.Responses["409"] = new OpenApiResponse
            {
                Description = $"Conflict: other writers kept changing the same data while the operation committed ({ICommitRejection.CommitConflictCode}); nothing was written and the same request may succeed when sent again.",
            };
        }

        if (operation.RequestBody is not null && !operation.Responses.ContainsKey("415"))
        {
            operation.Responses["415"] = new OpenApiResponse { Description = "Unsupported Media Type: the body is not in a media type the operation reads (see its requestBody)." };
        }

        foreach (var (status, response) in operation.Responses.Where(r => r.Key[0] is '4' or '5').ToArray())
        {
            if (response is OpenApiResponse problem)
            {
                Describe(problem, status, document);
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// The generator's own <c>ProblemDetails</c> is never answered: drop it once nothing refers to it. Drops the HEAD
    /// operations the endpoints' HEAD metadata adds next to every GET.
    /// </summary>
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Components?.Schemas?.Remove("ProblemDetails");
        foreach (var path in document.Paths.Values.OfType<OpenApiPathItem>())
        {
            path.Operations?.Remove(HttpMethod.Head);
        }

        return Task.CompletedTask;
    }

    /// <summary>Whether the operation can write: every method but the safe ones (RFC 9110 §9.2.1).</summary>
    private static bool Commits(OpenApiOperationTransformerContext context) =>
        context.Description.HttpMethod is not (null or "GET" or "HEAD" or "OPTIONS");

    /// <summary>
    /// Makes <paramref name="problem"/>, the <paramref name="status"/> answer of an operation, the Problem it really is:
    /// its body, <c>Content-Language</c>, and the headers of its status.
    /// </summary>
    internal static void Describe(OpenApiResponse problem, string status, OpenApiDocument document)
    {
        problem.Content = new Dictionary<string, IOpenApiMediaType>
        {
            [ProblemContentType] = new OpenApiMediaType { Schema = new OpenApiSchemaReference(ProblemSchema, document) },
        };
        problem.Headers ??= new Dictionary<string, IOpenApiHeader>();
        problem.Headers["Content-Language"] = new OpenApiHeader
        {
            Description = "The languages the problem's texts are written in.",
            Schema = new OpenApiSchema { Type = JsonSchemaType.String },
        };
        if (status == "429")
        {
            problem.Headers["Retry-After"] = new OpenApiHeader
            {
                Description = "Seconds to wait before the client's window reopens (also in the error's parameters.retryAfter).",
                Schema = new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int32", Minimum = "1" },
            };
        }
        else if (status == "401")
        {
            problem.Headers["WWW-Authenticate"] = new OpenApiHeader
            {
                Description = $"The scheme to authenticate with: {ErrorsProblemHttpResult.AuthenticationScheme} (the session token; a browser sends it in the session cookie).",
                Required = true,
                Schema = new OpenApiSchema { Type = JsonSchemaType.String },
            };
        }
        else if (status == "503")
        {
            problem.Headers["Retry-After"] = new OpenApiHeader
            {
                Description = string.Create(CultureInfo.InvariantCulture, $"Seconds to wait before trying again ({ErrorsProblemHttpResult.UnavailableRetryAfterSeconds})."),
                Schema = new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int32", Minimum = "1" },
            };
        }
    }

    private static void AddSchemas(OpenApiDocument document)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.Schemas ??= new Dictionary<string, IOpenApiSchema>();
        if (document.Components.Schemas.ContainsKey(ProblemSchema))
        {
            return;
        }

        document.Components.Schemas[ProblemErrorSchema] = new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            Description = "One error: its stable code, the values it was raised with and its text in the response language.",
            Required = new HashSet<string> { "code", "parameters" },
            Properties = new Dictionary<string, IOpenApiSchema>
            {
                ["code"] = Text("Stable error code (http.not_found)."),
                ["parameters"] = Parameters(),
                ["message"] = Text("The error in the language of Content-Language; absent when no text exists."),
            },
        };
        document.Components.Schemas[ProblemSchema] = new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            Description = "RFC 9457 Problem Details. type is the category of the failure; the error codes are only in errors, "
                + "which always lists every error, one or several.",
            Required = new HashSet<string> { "type", "title", "status", "instance", "errors", "traceId" },
            Properties = new Dictionary<string, IOpenApiSchema>
            {
                ["type"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.String,
                    Format = "uri",
                    Description = "The category of the failure: urn:frappe:problem: and a stable slug. It never names an error code. "
                        + "The list of categories grows: a client handles a category it does not know by its HTTP status.",

                    // Declared as an extensible enumeration, not a closed enum: a new category is not a breaking change,
                    // so a closed enum would make every one a breaking release of every endpoint.
                    Extensions = new Dictionary<string, IOpenApiExtension>
                    {
                        [ExtensibleEnum] = new JsonNodeExtension(new JsonArray([.. ProblemCategory.All.Select(category => JsonValue.Create(category.Type))])),
                    },
                },
                ["title"] = Text("Stable summary of the category, in the language of Content-Language."),
                ["status"] = new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int32", Description = "The HTTP status." },
                ["detail"] = Text("The error's text when there is one, or how many errors there are, in the language of Content-Language."),
                ["instance"] = Text("The request path."),
                ["errors"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.Array,
                    Items = new OpenApiSchemaReference(ProblemErrorSchema, document),
                    Description = "Every error found, so a client can fix them all at once.",
                },
                ["traceId"] = Text("W3C trace id of the request, to find it in the logs and traces."),
            },
            Examples =
            [
                JsonNode.Parse(string.Create(CultureInfo.InvariantCulture, $$"""
                {
                  "type": "urn:frappe:problem:not-found",
                  "title": "The resource does not exist.",
                  "status": 404,
                  "detail": "The URL does not match any endpoint of the API.",
                  "instance": "/v1/planets",
                  "errors": [{ "code": "http.not_found", "parameters": {}, "message": "The URL does not match any endpoint of the API." }],
                  "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01"
                }
                """))!,
            ],
        };
    }

    private static OpenApiSchema Text(string description) => new() { Type = JsonSchemaType.String, Description = description };

    private static OpenApiSchema Parameters() => new()
    {
        Type = JsonSchemaType.Object,
        Description = "The values the error was raised with, by name (the same the texts use).",
    };
}
