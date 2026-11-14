using System.Net;
using System.Text.Json;
using Frappe.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Frappe.Platform.Tests.Http;

/// <summary>Language negotiation, rate limiting and unexpected failures, through a real ASP.NET pipeline.</summary>
public sealed class HttpConventionsTests
{
    private sealed record Payload(string Name, int Count);

    private static async Task<WebApplication> StartAsync(int permitLimit = 100, bool throwOnBadRequest = false)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services
            .AddFrappeHttp(new AnonymousRateLimit(permitLimit, TimeSpan.FromMinutes(10)))
            .AddErrorMessages(typeof(HttpConventionsTests).Assembly);

        // Development hosts throw on a request the framework cannot read; production ones answer it directly.
        builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = throwOnBadRequest);

        var app = builder.Build();
        app.UseFrappeHttp();
        var v1 = app.MapGroup("/v1").AnswerHead().NegotiateRequestLanguage().RequireRateLimiting(HttpRegistration.AnonymousPolicy);
        v1.MapGet("/language", (HttpContext context) => context.Features.Get<RequestLanguage>()!.Tag.Value);
        v1.MapGet("/boom", string () => throw new InvalidOperationException("secret connection string"));
        v1.MapGet("/missing", () => Frappe.Results.Result.Failure<int>([new Frappe.Results.NotFoundError("test.english_only")]).ToHttpResult(TypedResults.Ok));
        v1.MapGet("/number", (int n) => n);
        v1.MapGet("/too-large", string () => throw new BadHttpRequestException("Request body too large.", StatusCodes.Status413PayloadTooLarge));
        v1.MapGet("/too-slow", string () => throw new BadHttpRequestException(
            "Reading the request body timed out due to data arriving too slowly.", StatusCodes.Status408RequestTimeout));
        v1.MapPut("/payload", (Payload payload) => payload.Name);
        v1.MapGet("/not-acceptable", () => Microsoft.AspNetCore.Http.Results.StatusCode(StatusCodes.Status406NotAcceptable));
        v1.MapGet("/private", () => TypedResults.Ok(new { secret = "personal" })).NoStore();
        v1.MapGet("/private-failure", () => Frappe.Results.Result.Failure<int>([new Frappe.Results.NotFoundError("test.english_only")]).ToHttpResult(TypedResults.Ok)).NoStore();
        app.MapGet("/free", () => "no language needed").AnswerHead();

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
        await using var app = await StartAsync();

        using var response = await GetAsync(app, "/v1/language", header);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await response.Content.ReadAsStringAsync()).IsEqualTo(expected);
        await Assert.That(response.Headers.Vary.ToArray()).Contains("Accept-Language");
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
        await using var app = await StartAsync();

        using var response = await GetAsync(app, "/v1/language", header);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await response.Content.ReadAsStringAsync()).IsEqualTo("en");
        await Assert.That(response.Headers.Vary.ToArray()).Contains("Accept-Language");
    }

    [Test]
    public async Task Endpoints_outside_the_group_need_no_language()
    {
        await using var app = await StartAsync();

        using var response = await GetAsync(app, "/free", acceptLanguage: null);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task An_unexpected_exception_is_a_500_problem_that_hides_the_exception()
    {
        await using var app = await StartAsync();

        using var response = await GetAsync(app, "/v1/boom", "es");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.InternalServerError);
        var body = await response.Content.ReadAsStringAsync();
        await Assert.That(body).DoesNotContain("secret");
        var problem = await ProblemAsync(response);
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

    [Test]
    public async Task Requests_beyond_the_limit_get_429_with_retry_after()
    {
        await using var app = await StartAsync(permitLimit: 2);

        using var first = await GetAsync(app, "/v1/language", "es");
        using var second = await GetAsync(app, "/v1/language", "es");
        using var third = await GetAsync(app, "/v1/language", acceptLanguage: null);

        await Assert.That(first.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(second.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(third.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
        await Assert.That(third.Headers.RetryAfter?.Delta).IsNotNull();
        await Assert.That(third.Headers.CacheControl?.NoStore).IsTrue();
        var problem = await ProblemAsync(third);
        await Assert.That(problem.GetProperty("type").GetString()).IsEqualTo("urn:frappe:problem:too-many-requests");
        await Assert.That(problem.GetProperty("errors")[0].GetProperty("code").GetString()).IsEqualTo(HttpRegistration.RateLimitedCode);
        await Assert.That(problem.GetProperty("status").GetInt32()).IsEqualTo(429);
        await Assert.That(problem.GetProperty("title").GetString()).IsEqualTo("Too many requests.");
        await Assert.That(third.Content.Headers.ContentLanguage.ToArray()).IsEquivalentTo(["en"]);
    }

    [Test]
    public async Task A_429_follows_Accept_Language_with_a_stable_title_and_the_wait_in_its_detail()
    {
        await using var app = await StartAsync(permitLimit: 1);

        using var first = await GetAsync(app, "/v1/language", "es-AR");
        using var limited = await GetAsync(app, "/v1/language", "fr, es-AR;q=0.8");

        await Assert.That(limited.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
        var seconds = (int)limited.Headers.RetryAfter!.Delta!.Value.TotalSeconds;
        await Assert.That(seconds).IsGreaterThan(0);
        await Assert.That(limited.Headers.Vary.ToArray()).Contains("Accept-Language");
        var problem = await ProblemAsync(limited);
        await Assert.That(problem.GetProperty("title").GetString()).IsEqualTo("Demasiadas solicitudes.");
        await Assert.That(problem.GetProperty("detail").GetString())
            .IsEqualTo($"Usted envió más solicitudes de las permitidas. Vuelva a intentarlo en {seconds} segundos.");
        await Assert.That(problem.TryGetProperty("parameters", out _)).IsFalse();
        var error = problem.GetProperty("errors").EnumerateArray().Single();
        await Assert.That(error.GetProperty("parameters").GetProperty("retryAfter").GetInt32()).IsEqualTo(seconds);
        await Assert.That(error.GetProperty("code").GetString()).IsEqualTo(HttpRegistration.RateLimitedCode);
        await Assert.That(error.GetProperty("message").GetString()).IsEqualTo(problem.GetProperty("detail").GetString());
        await Assert.That(limited.Content.Headers.ContentLanguage.ToArray()).IsEquivalentTo(["es", "es-AR"]);
    }

    [Test]
    [Arguments("/v1/language")]
    [Arguments("/free")]
    public async Task Every_GET_route_answers_HEAD_with_the_same_headers_and_no_body(string path)
    {
        await using var app = await StartAsync();

        using var get = await GetAsync(app, path, "es");
        using var head = await SendAsync(app, HttpMethod.Head, path, "es");

        await Assert.That(head.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(head.Content.Headers.ContentType?.ToString()).IsEqualTo(get.Content.Headers.ContentType?.ToString());
        await Assert.That(head.Headers.Vary.ToArray()).IsEquivalentTo(get.Headers.Vary.ToArray());
        await Assert.That((await head.Content.ReadAsByteArrayAsync()).Length).IsEqualTo(0);
    }

    [Test]
    [Arguments("/v1/nothing-here")]
    [Arguments("/elsewhere")]
    public async Task An_unknown_route_is_a_404_problem_in_the_requested_language(string path)
    {
        await using var app = await StartAsync();

        using var response = await GetAsync(app, path, "es");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        var problem = await ProblemAsync(response);
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

    [Test]
    public async Task A_method_the_route_does_not_answer_is_a_405_problem_that_keeps_Allow()
    {
        await using var app = await StartAsync();

        using var response = await SendAsync(app, HttpMethod.Delete, "/v1/language", acceptLanguage: null);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.MethodNotAllowed);
        await Assert.That(response.Content.Headers.Allow.ToArray()).IsEquivalentTo(["GET", "HEAD"]);
        var problem = await ProblemAsync(response);
        await Assert.That(problem.GetProperty("type").GetString()).IsEqualTo("urn:frappe:problem:method-not-allowed");
        await Assert.That(problem.GetProperty("errors")[0].GetProperty("code").GetString()).IsEqualTo(HttpRegistration.MethodNotAllowedCode);
        await Assert.That(problem.GetProperty("status").GetInt32()).IsEqualTo(405);
        await Assert.That(problem.GetProperty("title").GetString()).IsEqualTo("The method is not allowed for this resource.");
        await Assert.That(problem.GetProperty("detail").GetString()).IsEqualTo("DELETE is not allowed here. Allowed methods: GET, HEAD.");
        await Assert.That(problem.GetProperty("errors")[0].GetProperty("parameters").GetProperty("method").GetString()).IsEqualTo("DELETE");
        await Assert.That(response.Content.Headers.ContentLanguage.ToArray()).IsEquivalentTo(["en"]);
    }

    [Test]
    public async Task A_route_that_answers_its_own_problem_keeps_it()
    {
        await using var app = await StartAsync();

        using var response = await GetAsync(app, "/v1/missing", "es");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        var problem = await ProblemAsync(response);
        await Assert.That(problem.GetProperty("type").GetString()).IsEqualTo("urn:frappe:problem:not-found");
        await Assert.That(problem.GetProperty("errors")[0].GetProperty("code").GetString()).IsEqualTo("test.english_only");
    }

    [Test]
    public async Task A_parameter_the_framework_cannot_bind_is_a_400_problem_in_the_requested_language()
    {
        await using var app = await StartAsync();

        using var response = await GetAsync(app, "/v1/number?n=abc", "es");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
        await Assert.That(response.Headers.Vary.ToArray()).Contains("Accept-Language");
        await Assert.That(response.Content.Headers.ContentLanguage.ToArray()).IsEquivalentTo(["es"]);
        var problem = await ProblemAsync(response);
        await Assert.That(problem.GetProperty("type").GetString()).IsEqualTo("urn:frappe:problem:bad-request");
        await Assert.That(problem.GetProperty("status").GetInt32()).IsEqualTo(400);
        await Assert.That(problem.GetProperty("title").GetString()).IsEqualTo("La solicitud está mal formada.");
        await Assert.That(problem.GetProperty("detail").GetString())
            .IsEqualTo("La solicitud no se pudo leer: un valor no tiene el formato esperado.");
        await Assert.That(problem.GetProperty("instance").GetString()).IsEqualTo("/v1/number");
        await Assert.That(problem.GetProperty("errors")[0].GetProperty("code").GetString()).IsEqualTo(HttpRegistration.BadRequestCode);
        await Assert.That(problem.TryGetProperty("traceId", out _)).IsTrue();
    }

    [Test]
    public async Task A_body_the_server_refuses_as_too_large_is_a_413_problem()
    {
        await using var app = await StartAsync();

        using var response = await GetAsync(app, "/v1/too-large", "en");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.RequestEntityTooLarge);
        var problem = await ProblemAsync(response);
        await Assert.That(problem.GetProperty("type").GetString()).IsEqualTo("urn:frappe:problem:request-too-large");
        await Assert.That(problem.GetProperty("title").GetString()).IsEqualTo("The request is too large.");
        await Assert.That(problem.GetProperty("errors")[0].GetProperty("code").GetString()).IsEqualTo(HttpRegistration.RequestTooLargeCode);
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
    }

    [Test]
    public async Task Any_other_4xx_the_framework_rejects_a_request_with_keeps_its_status_as_a_bad_request_problem()
    {
        await using var app = await StartAsync();

        using var response = await GetAsync(app, "/v1/too-slow", "en");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.RequestTimeout);
        var problem = await ProblemAsync(response);
        await Assert.That(problem.GetProperty("status").GetInt32()).IsEqualTo(StatusCodes.Status408RequestTimeout);
        await Assert.That(problem.GetProperty("type").GetString()).IsEqualTo("urn:frappe:problem:bad-request");
        await Assert.That(problem.GetProperty("errors")[0].GetProperty("code").GetString()).IsEqualTo(HttpRegistration.BadRequestCode);
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
    }

    [Test]
    public async Task A_client_that_only_accepts_HTML_still_gets_the_problem_as_JSON_that_no_cache_keeps()
    {
        // The framework's writer skips such a client; a 404 without a body or no-store could be cached by a proxy.
        await using var app = await StartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/missing");
        request.Headers.TryAddWithoutValidation("Accept", "text/html");

        using var response = await app.GetTestClient().SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("application/problem+json");
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
        using var problem = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        await Assert.That(problem.RootElement.GetProperty("type").GetString()).IsEqualTo("urn:frappe:problem:not-found");
        await Assert.That(problem.RootElement.GetProperty("errors").GetArrayLength()).IsGreaterThan(0);
    }

    [Test]
    public async Task HEAD_is_declared_on_GET_endpoints_by_the_convention_not_rewritten_per_request()
    {
        await using var app = await StartAsync();

        var methods = app.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Single(endpoint => endpoint.RoutePattern.RawText == "/v1/language")
            .Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods;

        await Assert.That(methods).IsEquivalentTo(["GET", "HEAD"]);
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
        await using var app = await StartAsync(throwOnBadRequest: throwOnBadRequest);
        using var request = new HttpRequestMessage(HttpMethod.Put, "/v1/payload")
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("Accept-Language", "es-AR");

        using var response = await app.GetTestClient().SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest).Because(because);
        var problem = await ProblemAsync(response);
        await Assert.That(problem.GetProperty("type").GetString()).IsEqualTo("urn:frappe:problem:bad-request");
        await Assert.That(problem.GetProperty("title").GetString()).IsEqualTo("La solicitud está mal formada.");
        await Assert.That(problem.GetProperty("detail").GetString())
            .IsEqualTo("La solicitud no se pudo leer: un valor no tiene el formato esperado.");
        // Exactly one errors member, with Frappé codes only: never the framework's own (System.Text.Json text, type names).
        await Assert.That(problem.EnumerateObject().Count(member => member.Name == "errors")).IsEqualTo(1).Because(because);
        var error = problem.GetProperty("errors").EnumerateArray().Single();
        await Assert.That(error.GetProperty("code").GetString()).IsEqualTo(HttpRegistration.BadRequestCode);
        await Assert.That(error.GetProperty("message").GetString())
            .IsEqualTo("La solicitud no se pudo leer: un valor no tiene el formato esperado.");
        await Assert.That(response.Content.Headers.ContentLanguage.ToArray()).IsEquivalentTo(["es"]);
        await Assert.That(response.Headers.Vary.ToArray()).Contains("Accept-Language");
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task A_body_of_a_media_type_the_endpoint_does_not_accept_is_a_415_problem_naming_the_accepted_ones(bool throwOnBadRequest)
    {
        await using var app = await StartAsync(throwOnBadRequest: throwOnBadRequest);
        using var request = new HttpRequestMessage(HttpMethod.Put, "/v1/payload")
        {
            Content = new StringContent("{ \"name\": \"a\", \"count\": 1 }", System.Text.Encoding.UTF8, "text/plain"),
        };
        request.Headers.TryAddWithoutValidation("Accept-Language", "es");

        using var response = await app.GetTestClient().SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.UnsupportedMediaType);
        var problem = await ProblemAsync(response);
        await Assert.That(problem.GetProperty("type").GetString()).IsEqualTo("urn:frappe:problem:unsupported-media-type");
        await Assert.That(problem.GetProperty("status").GetInt32()).IsEqualTo(415);
        await Assert.That(problem.GetProperty("title").GetString()).IsEqualTo("El tipo de contenido de la solicitud no es aceptado.");
        var error = problem.GetProperty("errors").EnumerateArray().Single();
        await Assert.That(error.GetProperty("code").GetString()).IsEqualTo(HttpRegistration.UnsupportedMediaTypeCode);
        await Assert.That(error.GetProperty("parameters").GetProperty("contentType").GetString()).IsEqualTo("text/plain; charset=utf-8");
        await Assert.That(error.GetProperty("parameters").GetProperty("accepted")[0].GetString()).IsEqualTo("application/json");
        await Assert.That(problem.GetProperty("detail").GetString()).IsEqualTo("El cuerpo de la solicitud debe enviarse como application/json.");
        await Assert.That(response.Content.Headers.ContentLanguage.ToArray()).IsEquivalentTo(["es"]);
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
    }

    [Test]
    public async Task Any_other_4xx_answered_without_a_body_gets_a_bad_request_problem_that_keeps_its_status()
    {
        await using var app = await StartAsync();

        using var response = await GetAsync(app, "/v1/not-acceptable", "es");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotAcceptable);
        var problem = await ProblemAsync(response);
        await Assert.That(problem.GetProperty("status").GetInt32()).IsEqualTo(406);
        await Assert.That(problem.GetProperty("type").GetString()).IsEqualTo("urn:frappe:problem:bad-request");
        await Assert.That(problem.GetProperty("title").GetString()).IsEqualTo("La solicitud está mal formada.");
        await Assert.That(response.Content.Headers.ContentLanguage.ToArray()).IsEquivalentTo(["es"]);
    }

    [Test]
    public async Task A_no_store_endpoint_answers_its_success_with_no_store_and_no_validator()
    {
        await using var app = await StartAsync();

        using var response = await GetAsync(app, "/v1/private", "es");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
        await Assert.That(response.Headers.ETag).IsNull();
        var metadata = app.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Single(endpoint => endpoint.RoutePattern.RawText == "/v1/private")
            .Metadata.GetMetadata<NoStoreMetadata>();
        await Assert.That(metadata).IsNotNull();
    }

    [Test]
    public async Task A_no_store_endpoint_keeps_its_problems_as_they_are()
    {
        await using var app = await StartAsync();

        using var response = await GetAsync(app, "/v1/private-failure", "es");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
        var problem = await ProblemAsync(response);
        await Assert.That(problem.GetProperty("errors")[0].GetProperty("code").GetString()).IsEqualTo("test.english_only");
    }
}
