using System.Text.Json;
using Frappe.Platform.Localization;
using Frappe.Platform.Web.Correlation;
using Frappe.Platform.Web.Problems;
using Frappe.SharedKernel.Validation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Frappe.Platform.Web.Tests;

public sealed class ProblemResultsTests
{
    private static async Task<(HttpResponse Response, JsonElement Body)> Execute(Result result, string? acceptLanguage = null)
    {
        var services = new ServiceCollection().AddLogging()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().Build()).AddPlatformLocalization()
            .AddRequestCorrelation()
            .BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services.CreateScope().ServiceProvider };
        context.Response.Body = new MemoryStream();
        if (acceptLanguage is not null)
            context.Request.Headers.AcceptLanguage = acceptLanguage;

        // The middleware is what negotiates the locale; run it in front of the result like the pipeline does.
        var pipeline = new ApplicationBuilderStub(services).Build(ctx => result.ToProblem().ExecuteAsync(ctx));
        await pipeline(context);

        context.Response.Body.Position = 0;
        return (context.Response, await JsonSerializer.DeserializeAsync<JsonElement>(context.Response.Body));
    }

    [Theory]
    [InlineData(ErrorKind.Validation, 422)]
    [InlineData(ErrorKind.NotFound, 404)]
    [InlineData(ErrorKind.Conflict, 409)]
    [InlineData(ErrorKind.TooManyRequests, 429)]
    [InlineData(ErrorKind.Unauthorized, 401)]
    [InlineData(ErrorKind.Forbidden, 403)]
    public async Task SingleError_StatusFollowsItsKind_AndTypeNamesTheCode(ErrorKind kind, int status)
    {
        var (response, body) = await Execute(Result.Failure(new Error("sample.thing", "Thing failed.", Kind: kind)));

        response.StatusCode.ShouldBe(status);
        body.GetProperty("status").GetInt32().ShouldBe(status);
        body.GetProperty("type").GetString().ShouldBe("urn:frappe:problem:sample.thing");
    }

    [Fact]
    public async Task SeveralErrors_AreOneValidationProblem_TranslatedPerLocale()
    {
        var notification = new ValidationNotification()
            .Add(new Error("a.one", "One.", "name"))
            .Add(new Error("a.two", "Two.", Kind: ErrorKind.Conflict));

        var (response, body) = await Execute(Result.Failure(notification), "es-AR");

        response.StatusCode.ShouldBe(422);
        body.GetProperty("type").GetString().ShouldBe("urn:frappe:problem:validation");
        body.GetProperty("title").GetString().ShouldBe("Se incumplieron una o más reglas.");
        var errors = body.GetProperty("errors").EnumerateArray().ToList();
        errors.Select(e => e.GetProperty("code").GetString()).ShouldBe(["a.one", "a.two"]);
        errors[0].GetProperty("field").GetString().ShouldBe("name");
    }

    [Theory]
    [InlineData("pt-BR", "Muitas tentativas. Tente novamente mais tarde.")]
    [InlineData("es-MX", "Demasiados intentos. Inténtalo de nuevo más tarde.")]
    [InlineData("fr", "Too many attempts. Try again later.")]
    public async Task CatalogueEntryForTheCode_TranslatesTitleAndMessage(string acceptLanguage, string expected)
    {
        var (response, body) = await Execute(
            Result.Failure(new Error("platform.problem.429", "untranslated", Kind: ErrorKind.TooManyRequests)
                .WithRetryAfter(TimeSpan.FromSeconds(29.2))),
            acceptLanguage);

        body.GetProperty("title").GetString().ShouldBe(expected);
        body.GetProperty("errors")[0].GetProperty("message").GetString().ShouldBe(expected);
        response.Headers.RetryAfter.ToString().ShouldBe("30");
    }

    [Fact]
    public async Task TooManyRequests_WithoutAWait_SendsNoRetryAfter()
    {
        var (response, _) = await Execute(Result.Failure(new Error("sample.slow", "Slow down.", Kind: ErrorKind.TooManyRequests)));

        response.StatusCode.ShouldBe(429);
        response.Headers.ContainsKey("Retry-After").ShouldBeFalse();
    }

    [Fact]
    public async Task CodeWithoutCatalogueEntry_FallsBackToTheErrorMessage()
    {
        var (_, body) = await Execute(Result.Failure(new Error("module.unmigrated", "Plain English.")), "es-AR");

        body.GetProperty("title").GetString().ShouldBe("Plain English.");
        body.GetProperty("errors")[0].GetProperty("message").GetString().ShouldBe("Plain English.");
    }

    [Fact]
    public void Success_IsNotAProblem() => Should.Throw<InvalidOperationException>(() => Result.Success().ToProblem());

    [Fact]
    public void PlatformCatalogue_IsComplete() =>
        MessageCatalogConformance.Check(typeof(ProblemResults).Assembly).ShouldBeEmpty();
}
