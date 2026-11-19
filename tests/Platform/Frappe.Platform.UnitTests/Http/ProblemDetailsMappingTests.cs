using System.Text.Json;
using Frappe.Http;
using Frappe.Localization;
using Frappe.Platform.Testing;
using Frappe.Results;
using Frappe.UseCases;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Frappe.Platform.UnitTests.Http;

/// <summary>How a failed result becomes a problem response: status, type, texts in the caller's language and headers.</summary>
public sealed class ProblemDetailsMappingTests
{
    private static async Task<(int Status, IHeaderDictionary Headers, JsonElement Body)> ExecuteAsync(IResult httpResult, string? language)
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddFrappeHttp()
            .AddErrorMessages(PlatformTestMessages.Assembly)
            .BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Path = "/v1/orders/ZZZ/lines/3000";
        context.Response.Body = new MemoryStream();
        if (language is not null)
        {
            context.Features.Set(new RequestLanguage(LanguageTag.Create(language).Value));
        }

        await httpResult.ExecuteAsync(context);

        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        return (context.Response.StatusCode, context.Response.Headers, document.RootElement.Clone());
    }

    private static Result<int> TwoInvalidValues() => Result.Failure<int>(
    [
        Error.InvalidValue("orders.code.invalid_length", ("length", 2)),
        Error.InvalidValue("orders.quantity.out_of_range", ("min", 1900), ("max", 2100)),
    ]);

    [Test]
    public async Task Success_is_handed_to_the_success_projection()
    {
        // Arrange
        Result<int> result = 7;

        // Act
        var (status, _, body) = await ExecuteAsync(result.ToHttpResult(value => TypedResults.Ok(new { value })), "es");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(status).IsEqualTo(StatusCodes.Status200OK);
            await Assert.That(body.GetProperty("value").GetInt32()).IsEqualTo(7);
        }
    }

    [Test]
    public async Task A_successful_null_is_still_a_success()
    {
        // Arrange
        Result<string?> result = (string?)null;

        // Act
        var (untyped, _, body) = await ExecuteAsync(result.ToHttpResult(value => TypedResults.Ok(new { value })), "es");
        var (typed, _, _) = await ExecuteAsync(result.ToTypedHttpResult(value => TypedResults.Ok(new { value })), "es");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(untyped).IsEqualTo(StatusCodes.Status200OK);
            await Assert.That(typed).IsEqualTo(StatusCodes.Status200OK);
            await Assert.That(body.GetProperty("value").ValueKind).IsEqualTo(JsonValueKind.Null);
        }
    }

    [Test]
    public async Task The_typed_projection_answers_success_like_the_untyped_one()
    {
        // Arrange
        Result<int> result = 7;

        // Act
        var (status, _, body) = await ExecuteAsync(result.ToTypedHttpResult(value => TypedResults.Ok(new { value })), "es");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(status).IsEqualTo(StatusCodes.Status200OK);
            await Assert.That(body.GetProperty("value").GetInt32()).IsEqualTo(7);
        }
    }

    [Test]
    public async Task The_typed_projection_answers_failure_with_the_same_problem_as_the_untyped_one()
    {
        // Act
        var untyped = await ExecuteAsync(TwoInvalidValues().ToHttpResult(TypedResults.Ok), "es");
        var typed = await ExecuteAsync(TwoInvalidValues().ToTypedHttpResult(TypedResults.Ok), "es");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(typed.Status).IsEqualTo(untyped.Status);
            await Assert.That(typed.Headers.ContentType.ToString()).IsEqualTo(untyped.Headers.ContentType.ToString());
            await Assert.That(typed.Headers.ContentLanguage.ToString()).IsEqualTo(untyped.Headers.ContentLanguage.ToString());
            await Assert.That(WithoutTraceId(typed.Body)).IsEqualTo(WithoutTraceId(untyped.Body));
        }
    }

    /// <summary>The body as text without <c>traceId</c>, which differs per request.</summary>
    private static string WithoutTraceId(JsonElement body) =>
        JsonSerializer.Serialize(body.EnumerateObject().Where(p => p.Name != "traceId").ToDictionary(p => p.Name, p => p.Value));

    [Test]
    public async Task Several_errors_become_one_problem_listing_codes_and_parameters()
    {
        // Act
        var (status, headers, body) = await ExecuteAsync(TwoInvalidValues().ToHttpResult(TypedResults.Ok), "es");

        // Assert
        var errors = body.GetProperty("errors").EnumerateArray().ToArray();
        using (Assert.Multiple())
        {
            await Assert.That(status).IsEqualTo(StatusCodes.Status400BadRequest);
            await Assert.That(headers.ContentType.ToString()).IsEqualTo("application/problem+json");
            await Assert.That(body.GetProperty("type").GetString()).IsEqualTo("urn:frappe:problem:invalid-value");
            await Assert.That(body.GetProperty("status").GetInt32()).IsEqualTo(400);
            await Assert.That(body.GetProperty("instance").GetString()).IsEqualTo("/v1/orders/ZZZ/lines/3000");
            await Assert.That(body.TryGetProperty("traceId", out _)).IsTrue();
            await Assert.That(errors.Length).IsEqualTo(2);
            await Assert.That(errors[1].GetProperty("code").GetString()).IsEqualTo("orders.quantity.out_of_range");
            await Assert.That(errors[1].GetProperty("parameters").GetProperty("min").GetInt32()).IsEqualTo(1900);
        }
    }

    [Test]
    public async Task An_unavailable_dependency_answers_503_with_its_own_title_and_no_store()
    {
        // Arrange
        Result<int> result = new UnavailableError("test.code");

        // Act
        var (status, headers, body) = await ExecuteAsync(result.ToHttpResult(TypedResults.Ok), "es");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(status).IsEqualTo(503);
            await Assert.That(body.GetProperty("type").GetString()).IsEqualTo("urn:frappe:problem:unavailable");
            await Assert.That(body.GetProperty("title").GetString()).IsEqualTo("El servicio no está disponible en este momento.");
            await Assert.That(headers.CacheControl.ToString()).IsEqualTo("no-store");
            await Assert.That(headers.RetryAfter.ToString()).IsEqualTo("30");
        }
    }

    [Test]
    public async Task A_rate_limited_caller_answers_429_with_the_wait_rounded_up_to_whole_seconds()
    {
        // Arrange
        Result<int> result = new RateLimitedError("test.code", TimeSpan.FromSeconds(41.2));

        // Act
        var (status, headers, body) = await ExecuteAsync(result.ToHttpResult(TypedResults.Ok), "es");

        // Assert
        var error = body.GetProperty("errors")[0];
        using (Assert.Multiple())
        {
            await Assert.That(status).IsEqualTo(429);
            await Assert.That(body.GetProperty("type").GetString()).IsEqualTo("urn:frappe:problem:too-many-requests");
            await Assert.That(headers.RetryAfter.ToString()).IsEqualTo("42");
            await Assert.That(headers.CacheControl.ToString()).IsEqualTo("no-store");
            await Assert.That(error.GetProperty("code").GetString()).IsEqualTo("test.code");
            await Assert.That(error.GetProperty("parameters").GetProperty("retryAfter").GetInt32()).IsEqualTo(42);
        }
    }

    [Test]
    public async Task A_rate_limited_caller_waits_at_least_one_second()
    {
        // Arrange
        Result<int> result = new RateLimitedError("test.code", TimeSpan.FromMilliseconds(10));

        // Act
        var (_, headers, body) = await ExecuteAsync(result.ToHttpResult(TypedResults.Ok), "es");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(headers.RetryAfter.ToString()).IsEqualTo("1");
            await Assert.That(body.GetProperty("errors")[0].GetProperty("parameters").GetProperty("retryAfter").GetInt32()).IsEqualTo(1);
        }
    }

    [Test]
    public async Task Several_rate_limits_answer_the_longest_wait()
    {
        // Arrange
        var result = Result.Failure<int>([new RateLimitedError("a", TimeSpan.FromSeconds(5)), new RateLimitedError("b", TimeSpan.FromSeconds(90))]);

        // Act
        var (status, headers, _) = await ExecuteAsync(result.ToHttpResult(TypedResults.Ok), "es");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(status).IsEqualTo(429);
            await Assert.That(headers.RetryAfter.ToString()).IsEqualTo("90");
        }
    }

    [Test]
    public async Task A_rate_limit_wait_without_any_rate_limited_error_falls_back_to_the_default_instead_of_throwing()
    {
        // Act
        var withoutRateLimit = ErrorsProblemHttpResult.RateLimitedRetryAfterSeconds([new NotFoundError("a")]);
        var withoutErrors = ErrorsProblemHttpResult.RateLimitedRetryAfterSeconds([]);
        var mixed = ErrorsProblemHttpResult.RateLimitedRetryAfterSeconds([new RateLimitedError("a", TimeSpan.FromSeconds(7)), new NotFoundError("b")]);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(withoutRateLimit).IsEqualTo(ErrorsProblemHttpResult.DefaultRateLimitedRetryAfterSeconds);
            await Assert.That(withoutErrors).IsEqualTo(60);
            await Assert.That(mixed).IsEqualTo(7);
        }
    }

    [Test]
    public async Task An_unavailable_dependency_still_waits_30_seconds_beside_a_rate_limit()
    {
        // Arrange
        var result = Result.Failure<int>([new RateLimitedError("a", TimeSpan.FromSeconds(600)), new UnavailableError("b")]);

        // Act
        var (status, headers, _) = await ExecuteAsync(result.ToHttpResult(TypedResults.Ok), "es");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(status).IsEqualTo(503);
            await Assert.That(headers.RetryAfter.ToString()).IsEqualTo("30");
        }
    }

    [Test]
    public async Task An_unauthenticated_caller_answers_401_with_its_own_title_and_a_challenge()
    {
        // Arrange
        Result<int> result = new UnauthenticatedError("test.code");

        // Act
        var (status, headers, body) = await ExecuteAsync(result.ToHttpResult(TypedResults.Ok), "es");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(status).IsEqualTo(401);
            await Assert.That(body.GetProperty("type").GetString()).IsEqualTo("urn:frappe:problem:unauthenticated");
            await Assert.That(body.GetProperty("title").GetString()).IsEqualTo("La solicitud no está autenticada.");
            await Assert.That(headers.WWWAuthenticate.ToString()).IsEqualTo("Bearer");
            await Assert.That(headers.ContainsKey("Retry-After")).IsFalse();
        }
    }

    [Test]
    public async Task A_forbidden_caller_answers_403_with_its_own_title_and_no_challenge()
    {
        // Arrange
        Result<int> result = new ForbiddenError("test.code");

        // Act
        var (status, headers, body) = await ExecuteAsync(result.ToHttpResult(TypedResults.Ok), "es");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(status).IsEqualTo(403);
            await Assert.That(body.GetProperty("type").GetString()).IsEqualTo("urn:frappe:problem:forbidden");
            await Assert.That(body.GetProperty("title").GetString()).IsEqualTo("La operación no está permitida.");
            await Assert.That(headers.ContainsKey("WWW-Authenticate")).IsFalse();
        }
    }

    [Test]
    public async Task A_failed_precondition_answers_412_with_its_own_title_and_no_extra_headers()
    {
        // Arrange
        Result<int> result = new PreconditionFailedError("test.code", ("member", "x"));

        // Act
        var (status, headers, body) = await ExecuteAsync(result.ToHttpResult(TypedResults.Ok), "es");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(status).IsEqualTo(412);
            await Assert.That(body.GetProperty("type").GetString()).IsEqualTo("urn:frappe:problem:precondition-failed");
            await Assert.That(body.GetProperty("title").GetString()).IsEqualTo("La condición previa de la solicitud no se cumple.");
            await Assert.That(body.GetProperty("errors")[0].GetProperty("code").GetString()).IsEqualTo("test.code");
            await Assert.That(headers.ContainsKey("Retry-After")).IsFalse();
            await Assert.That(headers.ContainsKey("WWW-Authenticate")).IsFalse();
            await Assert.That(ProblemCategory.All).Contains(ProblemCategory.PreconditionFailed);
        }
    }

    [Test]
    public async Task A_commit_conflict_answers_409_with_its_own_text_in_the_requested_language()
    {
        // Arrange
        Result<int> result = new ConflictError(ICommitRejection.CommitConflictCode);

        // Act
        var (status, headers, body) = await ExecuteAsync(result.ToHttpResult(TypedResults.Ok), "es");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(status).IsEqualTo(StatusCodes.Status409Conflict);
            await Assert.That(body.GetProperty("type").GetString()).IsEqualTo("urn:frappe:problem:conflict");
            await Assert.That(body.GetProperty("errors")[0].GetProperty("code").GetString()).IsEqualTo("platform.persistence.commit_conflict");
            await Assert.That(body.GetProperty("detail").GetString()).IsEqualTo("Los datos fueron modificados por otra persona al mismo tiempo. Vuelva a intentarlo.");
            await Assert.That(headers.ContainsKey("Retry-After")).IsFalse();
            await Assert.That(headers.CacheControl.ToString()).IsEqualTo("no-store");
        }
    }

    [Test]
    public async Task A_rejection_raised_at_commit_is_answered_with_the_error_it_carries()
    {
        // Arrange
        ICommitRejection rejection = new LostRace();
        Result<int> result = rejection.Answer;

        // Act
        var (status, _, body) = await ExecuteAsync(result.ToHttpResult(TypedResults.Ok), "en");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(status).IsEqualTo(StatusCodes.Status409Conflict);
            await Assert.That(body.GetProperty("detail").GetString()).IsEqualTo("The data was changed by someone else at the same time. Try again.");
        }
    }

    /// <summary>A commit rejection as the storage adapter defines it, answering the platform's commit conflict.</summary>
    private sealed class LostRace : ICommitRejection
    {
        public Error Answer { get; } = new ConflictError(ICommitRejection.CommitConflictCode);
    }

    [Test]
    public async Task Only_an_unavailable_dependency_says_when_to_retry()
    {
        // Arrange
        Result<int> result = new NotFoundError("test.code");

        // Act
        var (status, headers, _) = await ExecuteAsync(result.ToHttpResult(TypedResults.Ok), "es");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(status).IsEqualTo(404);
            await Assert.That(headers.ContainsKey("Retry-After")).IsFalse();
        }
    }

    [Test]
    public async Task Texts_are_rendered_in_the_requested_language_from_the_catalog()
    {
        // Act
        var (_, headers, body) = await ExecuteAsync(TwoInvalidValues().ToHttpResult(TypedResults.Ok), "es-AR");

        // Assert
        var errors = body.GetProperty("errors").EnumerateArray().ToArray();
        using (Assert.Multiple())
        {
            await Assert.That(headers.ContentLanguage.ToString()).IsEqualTo("es");
            await Assert.That(body.GetProperty("title").GetString()).IsEqualTo("La solicitud contiene valores inválidos.");
            await Assert.That(body.GetProperty("detail").GetString()).IsEqualTo("Se encontraron 2 errores.");
            await Assert.That(errors[0].GetProperty("message").GetString()).IsEqualTo("El código del pedido debe tener 2 caracteres.");
            await Assert.That(errors[1].GetProperty("message").GetString()).IsEqualTo("La cantidad debe estar entre 1900 y 2100.");
        }
    }

    [Test]
    public async Task Without_a_negotiated_language_texts_are_in_English()
    {
        // Act
        var (status, headers, body) = await ExecuteAsync(TwoInvalidValues().ToHttpResult(TypedResults.Ok), language: null);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(status).IsEqualTo(StatusCodes.Status400BadRequest);
            await Assert.That(headers.ContentLanguage.ToString()).IsEqualTo("en");
            await Assert.That(body.GetProperty("title").GetString()).IsEqualTo("The request contains invalid values.");
            await Assert.That(body.GetProperty("detail").GetString()).IsEqualTo("2 errors were found.");
            await Assert.That(body.GetProperty("errors")[0].GetProperty("message").GetString()).IsEqualTo("The order code must have 2 characters.");
        }
    }

    [Test]
    public async Task A_language_without_messages_falls_back_to_English()
    {
        // Act
        var (_, headers, body) = await ExecuteAsync(TwoInvalidValues().ToHttpResult(TypedResults.Ok), "fr-CA");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(headers.ContentLanguage.ToString()).IsEqualTo("en");
            await Assert.That(body.GetProperty("title").GetString()).IsEqualTo("The request contains invalid values.");
        }
    }

    [Test]
    public async Task A_message_missing_in_the_requested_language_falls_back_alone_and_both_languages_are_declared()
    {
        // Arrange
        Result<int> result = Error.InvalidValue("test.english_only");

        // Act
        var (_, headers, body) = await ExecuteAsync(result.ToHttpResult(TypedResults.Ok), "es");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(body.GetProperty("title").GetString()).IsEqualTo("La solicitud contiene valores inválidos.");
            await Assert.That(body.GetProperty("detail").GetString()).IsEqualTo("Only in English.");
            await Assert.That(headers.ContentLanguage.ToString()).IsEqualTo("es, en");
        }
    }

    [Test]
    public async Task A_code_without_any_message_is_sent_as_code_only()
    {
        // Arrange
        Result<int> result = Error.InvalidValue("test.untranslated");

        // Act
        var (_, _, body) = await ExecuteAsync(result.ToHttpResult(TypedResults.Ok), "es");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(body.TryGetProperty("detail", out _)).IsFalse();
            await Assert.That(body.GetProperty("errors")[0].TryGetProperty("message", out _)).IsFalse();
            await Assert.That(body.GetProperty("errors")[0].GetProperty("code").GetString()).IsEqualTo("test.untranslated");
        }
    }

    [Test]
    [Arguments("es", 1, "Se encontró 1 error.")]
    [Arguments("es", 3, "Se encontraron 3 errores.")]
    [Arguments("en", 1, "1 error was found.")]
    [Arguments("en", 3, "3 errors were found.")]
    public async Task Messages_are_ICU_patterns_with_the_plural_rules_of_their_language(string language, int count, string expected)
    {
        // Arrange
        using var services = new ServiceCollection().AddLogging().AddErrorMessages(PlatformTestMessages.Assembly).BuildServiceProvider();

        // Act
        var rendered = services.GetRequiredService<ErrorMessageCatalog>()
            .Render("problem.several", LanguageTag.Create(language).Value, new Dictionary<string, object?> { ["count"] = count });

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(rendered?.Text).IsEqualTo(expected);
            await Assert.That(rendered?.Language.Value).IsEqualTo(language);
        }
    }

    [Test]
    [Arguments("es-AR", "Usted envió más solicitudes de las permitidas. Vuelva a intentarlo en 1 segundo.", "es-AR")]
    [Arguments("es-MX", "Usted envió más solicitudes de las permitidas. Vuelva a intentarlo en 1 segundo.", "es-MX")]
    [Arguments("es-CL", "Se enviaron más solicitudes de las permitidas. Vuelva a intentarlo en 1 segundo.", "es")]
    [Arguments("pt-PT", "Você enviou mais solicitações do que o permitido. Tente novamente em 1 segundo.", "pt-BR")]
    [Arguments("pt", "Você enviou mais solicitações do que o permitido. Tente novamente em 1 segundo.", "pt-BR")]
    [Arguments("de", "You sent more requests than allowed. Try again in 1 second.", "en")]
    public async Task Platform_messages_follow_the_supported_languages_and_their_fallbacks(string requested, string expected, string servedIn)
    {
        // Arrange
        using var services = new ServiceCollection().AddLogging().AddErrorMessages(PlatformTestMessages.Assembly).BuildServiceProvider();

        // Act
        var rendered = services.GetRequiredService<ErrorMessageCatalog>()
            .Render(HttpRegistration.RateLimitedCode, LanguageTag.Create(requested).Value, new Dictionary<string, object?> { ["retryAfter"] = 1 });

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(rendered?.Text).IsEqualTo(expected);
            await Assert.That(rendered?.Language.Value).IsEqualTo(servedIn);
        }
    }

    [Test]
    public async Task Messages_for_an_unsupported_language_fail_at_startup()
    {
        // Arrange
        var options = new ErrorMessageOptions();

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() => options.Add("Module", new Dictionary<string, Dictionary<string, string>>
        {
            ["en"] = new() { ["a"] = "A" },
            ["fr"] = new() { ["a"] = "A" },
        }));

        // Assert
        await Assert.That(exception.Message).Contains("'fr'");
    }

    [Test]
    public async Task A_message_missing_in_English_fails_at_startup()
    {
        // Arrange
        var options = new ErrorMessageOptions();

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() => options.Add("Module", new Dictionary<string, Dictionary<string, string>>
        {
            ["en"] = new() { ["a"] = "A" },
            ["es"] = new() { ["a"] = "A", ["b"] = "B" },
        }));

        // Assert
        await Assert.That(exception.Message).Contains("'b'");
    }

    [Test]
    public async Task Single_error_keeps_the_category_as_problem_type_and_renders_its_detail()
    {
        // Arrange
        Result<int> result = Error.InvalidValue("orders.quantity.out_of_range", ("min", 1900), ("max", 2100));

        // Act
        var (status, _, body) = await ExecuteAsync(result.ToHttpResult(TypedResults.Ok), "es");

        // Assert
        var error = body.GetProperty("errors").EnumerateArray().Single();
        using (Assert.Multiple())
        {
            await Assert.That(status).IsEqualTo(StatusCodes.Status400BadRequest);
            await Assert.That(body.GetProperty("type").GetString()).IsEqualTo("urn:frappe:problem:invalid-value");
            await Assert.That(body.TryGetProperty("code", out _)).IsFalse();
            await Assert.That(body.TryGetProperty("parameters", out _)).IsFalse();
            await Assert.That(body.GetProperty("detail").GetString()).IsEqualTo("La cantidad debe estar entre 1900 y 2100.");
            await Assert.That(error.GetProperty("code").GetString()).IsEqualTo("orders.quantity.out_of_range");
            await Assert.That(error.GetProperty("parameters").GetProperty("max").GetInt32()).IsEqualTo(2100);
            await Assert.That(error.GetProperty("message").GetString()).IsEqualTo("La cantidad debe estar entre 1900 y 2100.");
        }
    }

    [Test]
    public async Task Parameters_never_collide_with_problem_members()
    {
        // Arrange
        Result<int> result = new NotFoundError("test.code", ("type", "x"), ("errors", "y"), ("traceId", "z"), ("missing", null));

        // Act
        var (_, _, body) = await ExecuteAsync(result.ToHttpResult(TypedResults.Ok), "es");

        // Assert
        var parameters = body.GetProperty("errors")[0].GetProperty("parameters");
        using (Assert.Multiple())
        {
            await Assert.That(body.GetProperty("type").GetString()).IsEqualTo("urn:frappe:problem:not-found");
            await Assert.That(body.GetProperty("errors").ValueKind).IsEqualTo(JsonValueKind.Array);
            await Assert.That(parameters.GetProperty("type").GetString()).IsEqualTo("x");
            await Assert.That(parameters.GetProperty("errors").GetString()).IsEqualTo("y");
            await Assert.That(parameters.GetProperty("traceId").GetString()).IsEqualTo("z");
            await Assert.That(parameters.GetProperty("missing").ValueKind).IsEqualTo(JsonValueKind.Null);
        }
    }

    public static IEnumerable<Func<(Error[] Errors, int Status, string Type)>> MixedErrors()
    {
        yield return () => ([Error.InvalidValue("a"), new NotFoundError("b")], 404, "urn:frappe:problem:not-found");
        yield return () => ([Error.InvalidValue("a"), new ConflictError("b")], 409, "urn:frappe:problem:conflict");
        yield return () => ([Error.InvalidValue("a"), Error.RuleViolation("b")], 422, "urn:frappe:problem:rule-violation");
        yield return () => ([Error.RuleViolation("a"), new NotFoundError("b")], 404, "urn:frappe:problem:not-found");
        yield return () => ([new NotFoundError("a"), new ConflictError("b"), new UnavailableError("c")], 503, "urn:frappe:problem:unavailable");
        yield return () => ([Error.InvalidValue("a"), Error.RuleViolation("b"), new ConflictError("c"), new ForbiddenError("d")], 403, "urn:frappe:problem:forbidden");
        yield return () => ([new ForbiddenError("a"), new NotFoundError("b")], 404, "urn:frappe:problem:not-found");
        yield return () => ([new ForbiddenError("a"), new UnavailableError("b"), new UnauthenticatedError("c")], 401, "urn:frappe:problem:unauthenticated");
        yield return () => ([Error.InvalidValue("a"), Error.RuleViolation("b"), new ConflictError("c"), new ForbiddenError("d"), new NotFoundError("e"), new RateLimitedError("f", TimeSpan.FromSeconds(1))], 429, "urn:frappe:problem:too-many-requests");
        yield return () => ([new RateLimitedError("a", TimeSpan.FromSeconds(1)), new UnavailableError("b")], 503, "urn:frappe:problem:unavailable");
        yield return () => ([new RateLimitedError("a", TimeSpan.FromSeconds(1)), new UnauthenticatedError("b")], 401, "urn:frappe:problem:unauthenticated");
        yield return () => ([new PreconditionFailedError("a"), Error.InvalidValue("b")], 400, "urn:frappe:problem:invalid-value");
        yield return () => ([new PreconditionFailedError("a"), Error.RuleViolation("b"), new ConflictError("c")], 409, "urn:frappe:problem:conflict");
    }

    [Test]
    [MethodDataSource(nameof(MixedErrors))]
    public async Task Mixed_errors_answer_with_the_most_decisive_one_regardless_of_order(Error[] errors, int expectedStatus, string expectedType)
    {
        // Act
        var inOrder = await ExecuteAsync(Result.Failure<int>(errors).ToHttpResult(TypedResults.Ok), "es");
        var reversed = await ExecuteAsync(Result.Failure<int>([.. errors.Reverse()]).ToHttpResult(TypedResults.Ok), "es");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(inOrder.Status).IsEqualTo(expectedStatus);
            await Assert.That(inOrder.Body.GetProperty("type").GetString()).IsEqualTo(expectedType);
            await Assert.That(reversed.Status).IsEqualTo(expectedStatus);
            await Assert.That(reversed.Body.GetProperty("type").GetString()).IsEqualTo(expectedType);
        }
    }

    [Test]
    public async Task A_duplicated_message_template_fails_at_startup()
    {
        // Arrange
        using var services = new ServiceCollection()
            .AddLogging()
            .AddErrorMessages(PlatformTestMessages.Assembly)
            .Configure<ErrorMessageOptions>(options => options.Add("Duplicate", new Dictionary<string, Dictionary<string, string>>
            {
                ["en"] = new() { ["problem.invalid_value"] = "Duplicated." },
            }))
            .BuildServiceProvider();

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await services.GetRequiredService<IAsyncStartupValidator>().ValidateAsync(CancellationToken.None));

        // Assert
        await Assert.That(failure).IsNotNull();
    }

    [Test]
    public async Task Without_any_message_catalog_no_English_default_title_is_invented()
    {
        // Arrange
        using var services = new ServiceCollection().AddLogging().AddFrappeHttp().BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Response.Body = new MemoryStream();
        Result<int> result = new NotFoundError("test.code");

        await result.ToHttpResult(TypedResults.Ok).ExecuteAsync(context);

        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);

        // Act
        var body = document.RootElement;

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(body.GetProperty("type").GetString()).IsEqualTo("urn:frappe:problem:not-found");
            await Assert.That(body.TryGetProperty("title", out _)).IsFalse();
            await Assert.That(body.TryGetProperty("detail", out _)).IsFalse();
            await Assert.That(context.Response.Headers.ContentLanguage.Count).IsEqualTo(0);
        }
    }

    public static IEnumerable<Func<(Error Error, int Status)>> ErrorsAndStatuses()
    {
        yield return () => (Error.InvalidValue("test.code"), 400);
        yield return () => (Error.RuleViolation("test.code"), 422);
        yield return () => (new NotFoundError("test.code"), 404);
        yield return () => (new ConflictError("test.code"), 409);
        yield return () => (new UnavailableError("test.code"), 503);
        yield return () => (new UnauthenticatedError("test.code"), 401);
        yield return () => (new ForbiddenError("test.code"), 403);
        yield return () => (new RateLimitedError("test.code", TimeSpan.FromSeconds(1)), 429);
    }

    [Test]
    [MethodDataSource(nameof(ErrorsAndStatuses))]
    public async Task Error_type_decides_the_status_code(Error error, int expected)
    {
        // Arrange
        Result<int> result = error;

        // Act
        var (status, _, _) = await ExecuteAsync(result.ToHttpResult(TypedResults.Ok), "es");

        // Assert
        await Assert.That(status).IsEqualTo(expected);
    }

    [Test]
    public async Task Messages_without_English_fail_at_startup()
    {
        // Arrange
        var options = new ErrorMessageOptions();

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() => options.Add("Module", new Dictionary<string, Dictionary<string, string>>
        {
            ["es"] = new() { ["a"] = "A" },
        }));

        // Assert
        await Assert.That(exception.Message).Contains("English");
    }

    [Test]
    [Arguments("orders.quantity.out_of_range")]
    [Arguments("test.untranslated")]
    public async Task Problem_members_keep_their_order_whether_texts_are_rendered_or_not(string code)
    {
        // Arrange
        Result<int> result = Error.InvalidValue(code, ("min", 1900), ("max", 2100));

        var (_, _, body) = await ExecuteAsync(result.ToHttpResult(value => TypedResults.Ok(value)), "es");

        // Act
        string[] expected = code == "test.untranslated"
            ? ["type", "title", "status", "instance", "errors", "traceId"]
            : ["type", "title", "status", "detail", "instance", "errors", "traceId"];

        // Assert
        await Assert.That(string.Join(",", body.EnumerateObject().Select(member => member.Name))).IsEqualTo(string.Join(",", expected));
    }
}
