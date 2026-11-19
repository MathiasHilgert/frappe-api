using System.Net;
using System.Text.Json;
using Frappe.Http;
using Frappe.Platform.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Frappe.Platform.UnitTests.Http;

/// <summary>Language negotiation, unexpected failures and the problems of requests the framework rejects, through a real ASP.NET pipeline.</summary>
public sealed class HttpProblemPipelineTests
{
    /// <summary>The request body of the sample endpoints.</summary>
    private sealed record Payload(string Name, int Count);

    private static async Task<WebApplication> StartAsync(bool throwOnBadRequest = false)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services
            .AddFrappeHttp()
            .AddErrorMessages(PlatformTestMessages.Assembly);

        // Development hosts throw on a request the framework cannot read; production ones answer it directly.
        builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = throwOnBadRequest);

        var app = builder.Build();
        app.UseFrappeHttp();
        var v1 = app.MapGroup("/v1").NegotiateRequestLanguage();
        v1.MapGet("/language", (HttpContext context) => context.Features.Get<RequestLanguage>()!.Tag.Value);
        v1.MapGet("/boom", string () => throw new InvalidOperationException("secret connection string"));
        v1.MapGet("/missing", () => Frappe.Results.Result.Failure<int>([new Frappe.Results.NotFoundError("test.english_only")]).ToHttpResult(TypedResults.Ok));
        v1.MapGet("/number", (int n) => n);
        v1.MapGet("/too-large", string () => throw new BadHttpRequestException("Request body too large.", StatusCodes.Status413PayloadTooLarge));
        v1.MapGet("/too-slow", string () => throw new BadHttpRequestException(
            "Reading the request body timed out due to data arriving too slowly.", StatusCodes.Status408RequestTimeout));
        v1.MapPut("/payload", (Payload payload) => payload.Name);
        v1.MapPatch("/payload", (Payload payload) => payload.Name).Accepts<Payload>("application/merge-patch+json");
        v1.MapGet("/not-acceptable", () => Microsoft.AspNetCore.Http.Results.StatusCode(StatusCodes.Status406NotAcceptable));
        app.MapGet("/free", () => "no language needed");

        await app.StartAsync();
        return app;
    }

    private static Task<HttpResponseMessage> GetAsync(WebApplication app, string path, string? acceptLanguage) =>
        SendAsync(app, HttpMethod.Get, path, acceptLanguage);

    private static async Task<HttpResponseMessage> SendAsync(WebApplication app, HttpMethod method, string path, string? acceptLanguage)
    {
        using var request = new HttpRequestMessage(method, path);
        if (acceptLanguage is not null)
        {
            request.Headers.TryAddWithoutValidation("Accept-Language", acceptLanguage);
        }

        return await app.GetTestClient().SendAsync(request);
    }

    private static async Task<JsonElement> ProblemAsync(HttpResponseMessage response)
    {
        await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("application/problem+json");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    [Test]
    [Arguments("es-AR", "es-AR")]
    [Arguments("en;q=0.4, es-ar;q=0.9", "es-AR")]
    [Arguments("es, en", "es")]
    [Arguments("fr;q=0, en", "en")]
    [Arguments("fr, es-AR;q=0.8", "es-AR")]
    [Arguments("de, pt-PT;q=0.5, es;q=0.4", "pt-PT")]
    [Arguments("fr, es, pt", "es")]
    [Arguments("es-CL", "es-CL")]
    [Arguments("en-GB, es;q=0.9", "en-GB")]
    public async Task The_preferred_language_becomes_the_request_language(string header, string expected)
    {
        // Arrange
        await using var app = await StartAsync();

        // Act
        using var response = await GetAsync(app, "/v1/language", header);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(await response.Content.ReadAsStringAsync()).IsEqualTo(expected);
            await Assert.That(response.Headers.Vary.ToArray()).Contains("Accept-Language");
        }
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("   ")]
    [Arguments("*")]
    [Arguments("es_AR")]
    [Arguments("es-AR-x-private")]
    [Arguments("en;q=0")]
    [Arguments(";;;")]
    [Arguments("fr;q=en")]
    [Arguments("es;q=abc")]
    [Arguments("es;q=2")]
    [Arguments("fr")]
    [Arguments("de, fr;q=0.5, *;q=0.1")]
    public async Task Without_a_usable_language_the_request_is_served_in_English(string? header)
    {
        // Arrange
        await using var app = await StartAsync();

        // Act
        using var response = await GetAsync(app, "/v1/language", header);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(await response.Content.ReadAsStringAsync()).IsEqualTo("en");
            await Assert.That(response.Headers.Vary.ToArray()).Contains("Accept-Language");
        }
    }

    [Test]
    public async Task Endpoints_outside_the_group_need_no_language()
    {
        // Arrange
        await using var app = await StartAsync();

        // Act
        using var response = await GetAsync(app, "/free", acceptLanguage: null);

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task An_unexpected_exception_is_a_500_problem_that_hides_the_exception()
    {
        // Arrange
        await using var app = await StartAsync();

        // Act
        using var response = await GetAsync(app, "/v1/boom", "es");
        var body = await response.Content.ReadAsStringAsync();
        var problem = await ProblemAsync(response);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.InternalServerError);
            await Assert.That(body).DoesNotContain("secret");
            await Assert.That(problem.GetProperty("type").GetString()).IsEqualTo("urn:frappe:problem:unexpected");
            await Assert.That(problem.TryGetProperty("code", out _)).IsFalse();
            await Assert.That(problem.GetProperty("errors")[0].GetProperty("code").GetString()).IsEqualTo(HttpRegistration.UnexpectedCode);
            await Assert.That(problem.GetProperty("title").GetString()).IsEqualTo("Ocurrió un error inesperado.");
            await Assert.That(problem.GetProperty("detail").GetString())
                .IsEqualTo("La solicitud no se pudo completar. Si el problema persiste, informe el traceId.");
            await Assert.That(problem.GetProperty("traceId").GetString()).IsNotNull();
            await Assert.That(response.Content.Headers.ContentLanguage.ToArray()).IsEquivalentTo(["es"]);
            await Assert.That(response.Headers.Vary.ToArray()).Contains("Accept-Language");
        }
    }

    [Test]
    [Arguments("/v1/nothing-here")]
    [Arguments("/elsewhere")]
    public async Task An_unknown_route_is_a_404_problem_in_the_requested_language(string path)
    {
        // Arrange
        await using var app = await StartAsync();

        // Act
        using var response = await GetAsync(app, path, "es");
        var problem = await ProblemAsync(response);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
            await Assert.That(problem.TryGetProperty("code", out _)).IsFalse();
            await Assert.That(problem.GetProperty("type").GetString()).IsEqualTo("urn:frappe:problem:not-found");
            await Assert.That(problem.GetProperty("status").GetInt32()).IsEqualTo(404);
            await Assert.That(problem.GetProperty("title").GetString()).IsEqualTo("El recurso no existe.");
            await Assert.That(problem.GetProperty("detail").GetString()).IsEqualTo("La URL no corresponde a ningún endpoint de la API.");
            await Assert.That(problem.GetProperty("instance").GetString()).IsEqualTo(path);
            await Assert.That(problem.GetProperty("errors")[0].GetProperty("code").GetString()).IsEqualTo(HttpRegistration.NotFoundCode);
            await Assert.That(response.Content.Headers.ContentLanguage.ToArray()).IsEquivalentTo(["es"]);
            await Assert.That(response.Headers.Vary.ToArray()).Contains("Accept-Language");
        }
    }

    [Test]
    public async Task A_method_the_route_does_not_answer_is_a_405_problem_that_keeps_Allow()
    {
        // Arrange
        await using var app = await StartAsync();

        // Act
        using var response = await SendAsync(app, HttpMethod.Delete, "/v1/language", acceptLanguage: null);
        var problem = await ProblemAsync(response);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.MethodNotAllowed);
            await Assert.That(response.Content.Headers.Allow.ToArray()).IsEquivalentTo(["GET"]);
            await Assert.That(problem.GetProperty("type").GetString()).IsEqualTo("urn:frappe:problem:method-not-allowed");
            await Assert.That(problem.GetProperty("errors")[0].GetProperty("code").GetString()).IsEqualTo(HttpRegistration.MethodNotAllowedCode);
            await Assert.That(problem.GetProperty("status").GetInt32()).IsEqualTo(405);
            await Assert.That(problem.GetProperty("title").GetString()).IsEqualTo("The method is not allowed for this resource.");
            await Assert.That(problem.GetProperty("detail").GetString()).IsEqualTo("DELETE is not allowed here. Allowed methods: GET.");
            await Assert.That(problem.GetProperty("errors")[0].GetProperty("parameters").GetProperty("method").GetString()).IsEqualTo("DELETE");
            await Assert.That(response.Content.Headers.ContentLanguage.ToArray()).IsEquivalentTo(["en"]);
        }
    }

    [Test]
    public async Task A_route_that_answers_its_own_problem_keeps_it()
    {
        // Arrange
        await using var app = await StartAsync();

        // Act
        using var response = await GetAsync(app, "/v1/missing", "es");
        var problem = await ProblemAsync(response);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
            await Assert.That(problem.GetProperty("type").GetString()).IsEqualTo("urn:frappe:problem:not-found");
            await Assert.That(problem.GetProperty("errors")[0].GetProperty("code").GetString()).IsEqualTo("test.english_only");
        }
    }

    [Test]
    public async Task A_parameter_the_framework_cannot_bind_is_a_400_problem_in_the_requested_language()
    {
        // Arrange
        await using var app = await StartAsync();

        // Act
        using var response = await GetAsync(app, "/v1/number?n=abc", "es");
        var problem = await ProblemAsync(response);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
            await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
            await Assert.That(response.Headers.Vary.ToArray()).Contains("Accept-Language");
            await Assert.That(response.Content.Headers.ContentLanguage.ToArray()).IsEquivalentTo(["es"]);
            await Assert.That(problem.GetProperty("type").GetString()).IsEqualTo("urn:frappe:problem:bad-request");
            await Assert.That(problem.GetProperty("status").GetInt32()).IsEqualTo(400);
            await Assert.That(problem.GetProperty("title").GetString()).IsEqualTo("La solicitud está mal formada.");
            await Assert.That(problem.GetProperty("detail").GetString())
                .IsEqualTo("La solicitud no se pudo leer: un valor no tiene el formato esperado.");
            await Assert.That(problem.GetProperty("instance").GetString()).IsEqualTo("/v1/number");
            await Assert.That(problem.GetProperty("errors")[0].GetProperty("code").GetString()).IsEqualTo(HttpRegistration.BadRequestCode);
            await Assert.That(problem.TryGetProperty("traceId", out _)).IsTrue();
        }
    }

    [Test]
    public async Task A_body_the_server_refuses_as_too_large_is_a_413_problem()
    {
        // Arrange
        await using var app = await StartAsync();

        // Act
        using var response = await GetAsync(app, "/v1/too-large", "en");
        var problem = await ProblemAsync(response);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.RequestEntityTooLarge);
            await Assert.That(problem.GetProperty("type").GetString()).IsEqualTo("urn:frappe:problem:request-too-large");
            await Assert.That(problem.GetProperty("title").GetString()).IsEqualTo("The request is too large.");
            await Assert.That(problem.GetProperty("errors")[0].GetProperty("code").GetString()).IsEqualTo(HttpRegistration.RequestTooLargeCode);
            await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
        }
    }

    [Test]
    public async Task Any_other_4xx_the_framework_rejects_a_request_with_keeps_its_status_as_a_bad_request_problem()
    {
        // Arrange
        await using var app = await StartAsync();

        // Act
        using var response = await GetAsync(app, "/v1/too-slow", "en");
        var problem = await ProblemAsync(response);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.RequestTimeout);
            await Assert.That(problem.GetProperty("status").GetInt32()).IsEqualTo(StatusCodes.Status408RequestTimeout);
            await Assert.That(problem.GetProperty("type").GetString()).IsEqualTo("urn:frappe:problem:bad-request");
            await Assert.That(problem.GetProperty("errors")[0].GetProperty("code").GetString()).IsEqualTo(HttpRegistration.BadRequestCode);
            await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
        }
    }

    [Test]
    public async Task A_client_that_only_accepts_HTML_still_gets_the_problem_as_JSON_that_no_cache_keeps()
    {
        // The framework's writer skips such a client; a 404 without a body or no-store could be cached by a proxy.
        // Arrange
        await using var app = await StartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/missing");
        request.Headers.TryAddWithoutValidation("Accept", "text/html");

        // Act
        using var response = await app.GetTestClient().SendAsync(request);
        using var problem = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
            await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("application/problem+json");
            await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
            await Assert.That(problem.RootElement.GetProperty("type").GetString()).IsEqualTo("urn:frappe:problem:not-found");
            await Assert.That(problem.RootElement.GetProperty("errors").GetArrayLength()).IsGreaterThan(0);
        }
    }

    [Test]
    [Arguments(false, "{ \"name\": ", "invalid JSON")]
    [Arguments(false, "", "an empty body")]
    [Arguments(false, "{ \"name\": \"a\", \"count\": \"three\" }", "a value of the wrong type")]
    [Arguments(true, "{ \"name\": ", "invalid JSON")]
    [Arguments(true, "", "an empty body")]
    [Arguments(true, "{ \"name\": \"a\", \"count\": \"three\" }", "a value of the wrong type")]
    public async Task A_JSON_body_the_framework_cannot_read_is_a_400_problem_in_the_requested_language(bool throwOnBadRequest, string body, string because)
    {
        // Arrange
        await using var app = await StartAsync(throwOnBadRequest: throwOnBadRequest);
        using var request = new HttpRequestMessage(HttpMethod.Put, "/v1/payload")
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("Accept-Language", "es-AR");

        // Act
        using var response = await app.GetTestClient().SendAsync(request);
        var problem = await ProblemAsync(response);

        // Assert
        var error = problem.GetProperty("errors").EnumerateArray().Single();
        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest).Because(because);
            await Assert.That(problem.GetProperty("type").GetString()).IsEqualTo("urn:frappe:problem:bad-request");
            await Assert.That(problem.GetProperty("title").GetString()).IsEqualTo("La solicitud está mal formada.");
            await Assert.That(problem.GetProperty("detail").GetString())
                .IsEqualTo("La solicitud no se pudo leer: un valor no tiene el formato esperado.");
            // Exactly one errors member, with Frappé codes only: never the framework's own (System.Text.Json text, type names).
            await Assert.That(problem.EnumerateObject().Count(member => member.Name == "errors")).IsEqualTo(1).Because(because);
            await Assert.That(error.GetProperty("code").GetString()).IsEqualTo(HttpRegistration.BadRequestCode);
            await Assert.That(error.GetProperty("message").GetString())
                .IsEqualTo("La solicitud no se pudo leer: un valor no tiene el formato esperado.");
            await Assert.That(response.Content.Headers.ContentLanguage.ToArray()).IsEquivalentTo(["es"]);
            await Assert.That(response.Headers.Vary.ToArray()).Contains("Accept-Language");
            await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task A_body_of_a_media_type_the_endpoint_does_not_accept_is_a_415_problem_naming_the_accepted_ones(bool throwOnBadRequest)
    {
        // Arrange
        await using var app = await StartAsync(throwOnBadRequest: throwOnBadRequest);
        using var request = new HttpRequestMessage(HttpMethod.Put, "/v1/payload")
        {
            Content = new StringContent("{ \"name\": \"a\", \"count\": 1 }", System.Text.Encoding.UTF8, "text/plain"),
        };
        request.Headers.TryAddWithoutValidation("Accept-Language", "es");

        // Act
        using var response = await app.GetTestClient().SendAsync(request);
        var problem = await ProblemAsync(response);

        // Assert
        var error = problem.GetProperty("errors").EnumerateArray().Single();
        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.UnsupportedMediaType);
            await Assert.That(problem.GetProperty("type").GetString()).IsEqualTo("urn:frappe:problem:unsupported-media-type");
            await Assert.That(problem.GetProperty("status").GetInt32()).IsEqualTo(415);
            await Assert.That(problem.GetProperty("title").GetString()).IsEqualTo("El tipo de contenido de la solicitud no es aceptado.");
            await Assert.That(error.GetProperty("code").GetString()).IsEqualTo(HttpRegistration.UnsupportedMediaTypeCode);
            await Assert.That(error.GetProperty("parameters").GetProperty("contentType").GetString()).IsEqualTo("text/plain; charset=utf-8");
            await Assert.That(error.GetProperty("parameters").GetProperty("accepted")[0].GetString()).IsEqualTo("application/json");
            await Assert.That(problem.GetProperty("detail").GetString()).IsEqualTo("El cuerpo de la solicitud debe enviarse como application/json.");
            await Assert.That(response.Content.Headers.ContentLanguage.ToArray()).IsEquivalentTo(["es"]);
            await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
        }
    }

    [Test]
    public async Task A_patch_refused_for_its_media_type_names_merge_patch_in_Accept_Patch_and_in_the_problem()
    {
        // Arrange
        await using var app = await StartAsync();
        using var patch = new HttpRequestMessage(HttpMethod.Patch, "/v1/payload")
        {
            Content = new StringContent("{ \"name\": \"a\" }", System.Text.Encoding.UTF8, "application/json"),
        };
        using var put = new HttpRequestMessage(HttpMethod.Put, "/v1/payload")
        {
            Content = new StringContent("{ \"name\": \"a\" }", System.Text.Encoding.UTF8, "text/plain"),
        };

        using var patched = await app.GetTestClient().SendAsync(patch);

        // Act
        using var put415 = await app.GetTestClient().SendAsync(put);
        var error = (await ProblemAsync(patched)).GetProperty("errors").EnumerateArray().Single();

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(patched.StatusCode).IsEqualTo(HttpStatusCode.UnsupportedMediaType);
            await Assert.That(patched.Headers.TryGetValues("Accept-Patch", out var acceptPatch) ? string.Join(", ", acceptPatch) : null)
                .IsEqualTo("application/merge-patch+json");
            await Assert.That(error.GetProperty("parameters").GetProperty("accepted")[0].GetString()).IsEqualTo("application/merge-patch+json");
            // Accept-Patch is about PATCH (RFC 5789): a 415 to any other method does not carry it.
            await Assert.That(put415.StatusCode).IsEqualTo(HttpStatusCode.UnsupportedMediaType);
            await Assert.That(put415.Headers.Contains("Accept-Patch")).IsFalse();
        }
    }

    [Test]
    public async Task Any_other_4xx_answered_without_a_body_gets_a_bad_request_problem_that_keeps_its_status()
    {
        // Arrange
        await using var app = await StartAsync();

        // Act
        using var response = await GetAsync(app, "/v1/not-acceptable", "es");
        var problem = await ProblemAsync(response);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotAcceptable);
            await Assert.That(problem.GetProperty("status").GetInt32()).IsEqualTo(406);
            await Assert.That(problem.GetProperty("type").GetString()).IsEqualTo("urn:frappe:problem:bad-request");
            await Assert.That(problem.GetProperty("title").GetString()).IsEqualTo("La solicitud está mal formada.");
            await Assert.That(response.Content.Headers.ContentLanguage.ToArray()).IsEquivalentTo(["es"]);
        }
    }

}
