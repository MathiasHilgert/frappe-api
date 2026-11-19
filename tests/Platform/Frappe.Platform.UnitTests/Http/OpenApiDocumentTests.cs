using System.Text.Json;
using System.Text.Json.Serialization;
using Frappe.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Frappe.Platform.UnitTests.Http;

/// <summary>The API document: what every endpoint shares is described once, by the platform, and its <c>info</c> comes from the host.</summary>
public sealed class OpenApiDocumentTests
{
    /// <summary>An order's state, written by name.</summary>
    public enum OrderState
    {
        /// <summary>Not yet shipped.</summary>
        Open,

        /// <summary>On its way.</summary>
        Shipped,
    }

    /// <summary>The body of an order.</summary>
    public sealed record OrderBody(string Id, OrderState State);

    private static async Task<JsonElement> DocumentAsync(Dictionary<string, string?>? settings = null)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(settings ?? []);
        builder.Services.AddFrappeHttp();

        // The host writes enums by name; the generated schema lists the values without saying they are text.
        builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        await using var app = builder.Build();
        app.UseFrappeHttp();
        app.MapOpenApi();
        var v1 = app.MapGroup("/v1").NegotiateRequestLanguage();
        v1.MapGet("/orders", (HttpContext _) => TypedResults.Ok(new OrderBody("1", OrderState.Open))).WithMetadata(new CacheableMetadata(TimeSpan.FromHours(1)));
        v1.MapGet("/orders/live", () => TypedResults.Ok(new OrderBody("1", OrderState.Open))).WithMetadata(new NoCacheMetadata());
        v1.MapGet("/orders/secret", () => TypedResults.Ok(new OrderBody("1", OrderState.Open))).WithMetadata(new NoStoreMetadata());
        v1.MapGet("/orders/search", ([DocumentedParameter(Required = true, Format = ParameterFormat.WholeNumber, Minimum = 1, Maximum = 50)] string? limit,
            [DocumentedParameter(Values = ["open", "shipped"])] string? state,
            [DocumentedParameter(Format = ParameterFormat.Date, Pattern = "^x$")] string? since,
            [DocumentedParameter(Format = ParameterFormat.Uuid)] string? id,
            string? untouched) => TypedResults.Ok());
        v1.MapPost("/orders", (OrderBody body) => TypedResults.Created("/v1/orders/1", body)).WithMetadata(new IdempotentMetadata());
        v1.MapPatch("/orders/{id}", (string id, OrderBody body) => TypedResults.Ok(body)).DeclaresAcceptPatch("the order").DeclaresIfMatch("The order's tag.");
        v1.MapDelete("/orders/{id}", (string id) => TypedResults.NoContent()).AddOpenApiOperationTransformer((operation, _, _) =>
        {
            operation.Responses ??= [];
            operation.Responses["403"] = new Microsoft.OpenApi.OpenApiResponse { Description = "Forbidden: only the order's owner may cancel it (orders.cancel.forbidden)." };
            return Task.CompletedTask;
        });
        v1.MapGet("/orders/tagged", () => TypedResults.Ok(new OrderBody("1", OrderState.Open))).SendsNoStoreETag("The order's version.", "no-store");
        await app.StartAsync();

        var text = await app.GetTestClient().GetStringAsync("/openapi/v1.json");
        return JsonElement.Parse(text);
    }

    private static JsonElement Operation(JsonElement document, string path, string method) => document.GetProperty("paths").GetProperty(path).GetProperty(method);

    [Test]
    public async Task The_info_comes_from_the_host_configuration()
    {
        // Act
        var document = await DocumentAsync(new()
        {
            ["Api:Document:Title"] = "Orders API",
            ["Api:Document:Version"] = "2.1.0",
            ["Api:Document:Description"] = "Places orders.",
        });

        // Assert
        var info = document.GetProperty("info");
        using (Assert.Multiple())
        {
            await Assert.That(info.GetProperty("title").GetString()).IsEqualTo("Orders API");
            await Assert.That(info.GetProperty("version").GetString()).IsEqualTo("2.1.0");
            await Assert.That(info.GetProperty("description").GetString()).IsEqualTo("Places orders.");
        }
    }

    [Test]
    public async Task Without_configuration_the_info_is_neutral_and_has_no_description()
    {
        // Act
        var info = (await DocumentAsync()).GetProperty("info");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(info.GetProperty("title").GetString()).IsEqualTo("API");
            await Assert.That(info.GetProperty("version").GetString()).IsEqualTo("1.0.0");
            await Assert.That(info.TryGetProperty("description", out _)).IsFalse();
        }
    }

    [Test]
    public async Task A_blank_title_or_version_is_a_startup_failure_naming_each_key()
    {
        // Arrange
        var options = new ApiDocumentOptions { Title = " ", Version = string.Empty };

        // Act
        var result = new ApiDocumentOptionsValidator().Validate(null, options);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.Failed).IsTrue();
            await Assert.That(result.Failures).IsEquivalentTo(
            [
                "Configuration 'Api:Document:Title' must not be empty.",
                "Configuration 'Api:Document:Version' must not be empty.",
            ]);
        }
    }

    [Test]
    public async Task Valid_options_pass_validation()
    {
        // Act
        var result = new ApiDocumentOptionsValidator().Validate(null, new ApiDocumentOptions());

        // Assert
        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task Every_operation_answers_problems_with_the_shared_schema_and_the_document_keeps_no_head_operation()
    {
        // Act
        var document = await DocumentAsync();

        // Assert
        var get = Operation(document, "/v1/orders", "get");
        var post = Operation(document, "/v1/orders", "post");
        using (Assert.Multiple())
        {
            await Assert.That(document.GetProperty("components").GetProperty("schemas").TryGetProperty("Problem", out _)).IsTrue();
            await Assert.That(document.GetProperty("components").GetProperty("schemas").TryGetProperty("ProblemDetails", out _)).IsFalse();
            await Assert.That(get.GetProperty("responses").GetProperty("500").GetProperty("content").TryGetProperty("application/problem+json", out _)).IsTrue();
            await Assert.That(get.GetProperty("responses").GetProperty("503").GetProperty("headers").TryGetProperty("Retry-After", out _)).IsTrue();
            await Assert.That(get.GetProperty("responses").TryGetProperty("409", out _)).IsFalse();
            await Assert.That(post.GetProperty("responses").GetProperty("409").GetProperty("description").GetString()).Contains("platform.persistence.commit_conflict");
            await Assert.That(post.GetProperty("responses").GetProperty("415").GetProperty("headers").TryGetProperty("Content-Language", out _)).IsTrue();
            await Assert.That(document.GetProperty("paths").GetProperty("/v1/orders").TryGetProperty("head", out _)).IsFalse();
        }
    }

    [Test]
    public async Task Every_unsafe_operation_documents_the_origin_forbidden_answer()
    {
        // Act
        var document = await DocumentAsync();

        // Assert
        var forbidden = Operation(document, "/v1/orders", "post").GetProperty("responses").GetProperty("403");
        using (Assert.Multiple())
        {
            await Assert.That(forbidden.GetProperty("description").GetString()).Contains("http.origin.forbidden");
            await Assert.That(document.GetProperty("paths").GetProperty("/v1/orders").GetProperty("get").GetProperty("responses").TryGetProperty("403", out _)).IsFalse();
        }
    }

    [Test]
    public async Task The_origin_forbidden_answer_joins_a_403_the_operation_already_declares()
    {
        // Act
        var document = await DocumentAsync();

        // Assert
        var forbidden = Operation(document, "/v1/orders/{id}", "delete").GetProperty("responses").GetProperty("403");
        var description = forbidden.GetProperty("description").GetString();
        using (Assert.Multiple())
        {
            await Assert.That(description).StartsWith("Forbidden: only the order's owner may cancel it (orders.cancel.forbidden).");
            await Assert.That(description).Contains("http.origin.forbidden");
            await Assert.That(forbidden.GetProperty("content").TryGetProperty("application/problem+json", out _)).IsTrue();
        }
    }

    [Test]
    public async Task A_language_negotiating_operation_declares_Accept_Language_and_Content_Language()
    {
        // Act
        var get = Operation(await DocumentAsync(), "/v1/orders", "get");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(get.GetProperty("parameters").EnumerateArray().Any(p => p.GetProperty("name").GetString() == "Accept-Language")).IsTrue();
            await Assert.That(get.GetProperty("responses").GetProperty("200").GetProperty("headers").TryGetProperty("Content-Language", out _)).IsTrue();
        }
    }

    [Test]
    public async Task A_cacheable_operation_documents_its_validators_and_the_not_modified_answer()
    {
        // Act
        var get = Operation(await DocumentAsync(), "/v1/orders", "get");

        // Assert
        var ok = get.GetProperty("responses").GetProperty("200").GetProperty("headers");
        var notModified = get.GetProperty("responses").GetProperty("304").GetProperty("headers");
        using (Assert.Multiple())
        {
            await Assert.That(ok.GetProperty("Cache-Control").GetProperty("description").GetString()).Contains("max-age=3600");
            await Assert.That(ok.TryGetProperty("ETag", out _)).IsTrue();
            await Assert.That(ok.TryGetProperty("Vary", out _)).IsTrue();
            await Assert.That(notModified.TryGetProperty("ETag", out _)).IsTrue();
            await Assert.That(get.GetProperty("parameters").EnumerateArray().Any(p => p.GetProperty("name").GetString() == "If-None-Match")).IsTrue();
        }
    }

    [Test]
    public async Task A_revalidated_operation_documents_no_cache()
    {
        // Act
        var get = Operation(await DocumentAsync(), "/v1/orders/live", "get");

        // Assert
        await Assert.That(get.GetProperty("responses").GetProperty("200").GetProperty("headers").GetProperty("Cache-Control").GetProperty("description").GetString()).Contains("no-cache");
    }

    [Test]
    public async Task A_never_stored_operation_documents_no_store_and_no_validator()
    {
        // Act
        var get = Operation(await DocumentAsync(), "/v1/orders/secret", "get");

        // Assert
        var headers = get.GetProperty("responses").GetProperty("200").GetProperty("headers");
        using (Assert.Multiple())
        {
            await Assert.That(headers.GetProperty("Cache-Control").GetProperty("description").GetString()).Contains("no-store");
            await Assert.That(headers.TryGetProperty("ETag", out _)).IsFalse();
            await Assert.That(get.GetProperty("responses").TryGetProperty("304", out _)).IsFalse();
        }
    }

    [Test]
    public async Task An_idempotent_operation_documents_the_key_and_the_replayed_header()
    {
        // Act
        var post = Operation(await DocumentAsync(), "/v1/orders", "post");

        // Assert
        var key = post.GetProperty("parameters").EnumerateArray().Single(p => p.GetProperty("name").GetString() == "Idempotency-Key");
        using (Assert.Multiple())
        {
            await Assert.That(key.GetProperty("in").GetString()).IsEqualTo("header");
            await Assert.That(key.GetProperty("schema").GetProperty("maxLength").GetInt32()).IsEqualTo(255);
            await Assert.That(post.GetProperty("responses").GetProperty("201").GetProperty("headers").TryGetProperty("Idempotent-Replayed", out _)).IsTrue();
            await Assert.That(post.GetProperty("responses").GetProperty("409").GetProperty("description").GetString()).Contains("platform.idempotency_key.in_progress");
        }
    }

    [Test]
    public async Task A_created_answer_declares_its_Location()
    {
        // Act
        var created = Operation(await DocumentAsync(), "/v1/orders", "post").GetProperty("responses").GetProperty("201");

        // Assert
        await Assert.That(created.GetProperty("headers").GetProperty("Location").GetProperty("required").GetBoolean()).IsTrue();
    }

    [Test]
    public async Task Documented_query_parameters_are_typed_by_their_attribute()
    {
        // Act
        var parameters = Operation(await DocumentAsync(), "/v1/orders/search", "get").GetProperty("parameters").EnumerateArray().ToDictionary(p => p.GetProperty("name").GetString()!);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(parameters["limit"].GetProperty("required").GetBoolean()).IsTrue();
            await Assert.That(parameters["limit"].GetProperty("schema").GetProperty("format").GetString()).IsEqualTo("int32");
            await Assert.That(parameters["limit"].GetProperty("schema").GetProperty("minimum").ToString()).IsEqualTo("1");
            await Assert.That(parameters["limit"].GetProperty("schema").GetProperty("maximum").ToString()).IsEqualTo("50");
            await Assert.That(parameters["state"].GetProperty("schema").GetProperty("enum").EnumerateArray().Select(v => v.GetString()!)).IsEquivalentTo(["open", "shipped"]);
            await Assert.That(parameters["since"].GetProperty("schema").GetProperty("format").GetString()).IsEqualTo("date");
            await Assert.That(parameters["since"].GetProperty("schema").GetProperty("pattern").GetString()).IsEqualTo("^x$");
            await Assert.That(parameters["id"].GetProperty("schema").GetProperty("format").GetString()).IsEqualTo("uuid");
            await Assert.That(parameters["untouched"].GetProperty("schema").TryGetProperty("format", out _)).IsFalse();
        }
    }

    [Test]
    public async Task An_enum_is_documented_as_a_string()
    {
        // Act
        var schema = (await DocumentAsync()).GetProperty("components").GetProperty("schemas").GetProperty("OrderState");

        // Assert
        await Assert.That(schema.GetProperty("type").GetString()).IsEqualTo("string");
    }

    [Test]
    public async Task A_patch_declares_Accept_Patch_and_If_Match()
    {
        // Act
        var patch = Operation(await DocumentAsync(), "/v1/orders/{id}", "patch");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(patch.GetProperty("responses").GetProperty("200").GetProperty("headers").GetProperty("Accept-Patch").GetProperty("description").GetString()).Contains("the order");
            await Assert.That(patch.GetProperty("responses").GetProperty("415").GetProperty("headers").TryGetProperty("Accept-Patch", out _)).IsTrue();
            await Assert.That(patch.GetProperty("parameters").EnumerateArray().Any(p => p.GetProperty("name").GetString() == "If-Match")).IsTrue();
        }
    }

    [Test]
    public async Task A_no_store_ETag_is_declared_required_on_the_success()
    {
        // Act
        var headers = Operation(await DocumentAsync(), "/v1/orders/tagged", "get").GetProperty("responses").GetProperty("200").GetProperty("headers");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(headers.GetProperty("ETag").GetProperty("required").GetBoolean()).IsTrue();
            await Assert.That(headers.GetProperty("Cache-Control").GetProperty("description").GetString()).IsEqualTo("no-store");
        }
    }
}
