using System.Text.Json;
using Alba;
using Frappe.Platform.Localization;
using Frappe.SharedKernel.Localization;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Frappe.IntegrationTests;

public sealed class LocalizationTests(ApiFixture api)
{
    private static async Task<IScenarioResult> Get(IAlbaHost host, string url, string? acceptLanguage, int status = 200) =>
        await host.Scenario(s =>
        {
            s.Get.Url(url);
            if (acceptLanguage is not null)
                s.WithRequestHeader("Accept-Language", acceptLanguage);
            s.StatusCodeShouldBe(status);
        });

    [Theory]
    [InlineData(null, "en")]
    [InlineData("es-AR", "es-AR")]
    [InlineData("es-CL,en;q=0.5", "es-AR")]
    [InlineData("fr, pt-BR;q=0.3", "pt-BR")]
    [InlineData("de", "en")]
    public async Task EveryResponse_DeclaresItsLanguage_AndVariesOnAcceptLanguage(string? acceptLanguage, string expected)
    {
        var result = await Get(api.Host, "/health/live", acceptLanguage);

        result.Context.Response.Headers.ContentLanguage.ToString().ShouldBe(expected);
        result.Context.Response.Headers.Vary.ToString().ShouldContain("Accept-Language", Case.Insensitive);
    }

    [Theory]
    [InlineData(null, "pt-BR")]
    [InlineData("de", "pt-BR")]
    [InlineData("es-MX", "es-MX")]
    public async Task StoredPreference_AppliesUnlessTheHeaderCanBeServed(string? acceptLanguage, string expected)
    {
        await using var host = await api.CreateHostAsync(services =>
            services.AddScoped<IPreferredLocaleSource, PrefersPortuguese>());

        var result = await Get(host, "/health/live", acceptLanguage);

        result.Context.Response.Headers.ContentLanguage.ToString().ShouldBe(expected);
    }

    [Fact]
    public async Task FrameworkProblems_AreTranslated()
    {
        var result = await Get(api.Host, "/v1/no-such-route", "es-AR", 401);

        result.Context.Response.Headers.ContentLanguage.ToString().ShouldBe("es-AR");
        var body = await result.ReadAsJsonAsync<JsonElement>();
        body.GetProperty("title").GetString().ShouldBe("Tenés que iniciar sesión.");
    }

    private sealed class PrefersPortuguese : IPreferredLocaleSource
    {
        public ValueTask<Locale?> GetPreferredLocaleAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<Locale?>(Locale.Parse("pt-BR"));
    }
}
