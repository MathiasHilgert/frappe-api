using Frappe.SharedKernel.Localization;
using Shouldly;

namespace Frappe.Platform.Localization.Tests;

public sealed class LocaleNegotiatorTests
{
    private static readonly SupportedLocales Supported =
        LocalizationOptions.Defaults;

    private readonly LocaleNegotiator _negotiator = new(Supported);

    [Theory]
    [InlineData("es-AR", "es-AR")]
    [InlineData("es-ar", "es-AR")]
    [InlineData("pt-BR,en;q=0.5", "pt-BR")]
    [InlineData("es-CL", "es-AR")]
    [InlineData("es", "es-AR")]
    [InlineData("pt-PT", "pt-BR")]
    [InlineData("en-GB", "en")]
    [InlineData("fr-FR, es-MX;q=0.8", "es-MX")]
    [InlineData("en;q=0.4, pt-BR;q=0.9", "pt-BR")]
    [InlineData("pt-BR;q=0, es-MX", "es-MX")]
    [InlineData("not a tag!!, es-MX;q=0.1", "es-MX")]
    [InlineData("*", "en")]
    [InlineData("fr", "en")]
    [InlineData("", "en")]
    [InlineData(null, "en")]
    public void Negotiate_WithoutPreference_LooksUpTheHeader(string? header, string expected) =>
        _negotiator.Negotiate(header, preferred: null).ShouldBe(Locale.Parse(expected));

    [Fact]
    public void Negotiate_SupportedHeader_WinsOverThePreference() =>
        _negotiator.Negotiate("en-US", Locale.Parse("es-AR")).ShouldBe(Locale.Parse("en"));

    [Theory]
    [InlineData(null)]
    [InlineData("fr")]
    public void Negotiate_WithoutAServableHeader_UsesThePreference(string? header) =>
        _negotiator.Negotiate(header, Locale.Parse("pt-BR")).ShouldBe(Locale.Parse("pt-BR"));

    [Fact]
    public void Negotiate_UnsupportedPreference_FallsBackByLanguageThenDefault()
    {
        _negotiator.Negotiate(null, Locale.Parse("es-ES")).ShouldBe(Locale.Parse("es-AR"));
        _negotiator.Negotiate(null, Locale.Parse("de")).ShouldBe(Locale.Parse("en"));
    }

    [Fact]
    public void FallbackChain_GoesThroughTheLanguageThenTheDefault() =>
        Supported.FallbackChain(Locale.Parse("es-MX")).ShouldBe([Locale.Parse("es-MX"), Locale.Parse("es-AR"), Locale.Parse("en")]);

    [Fact]
    public void SupportedLocales_RequireTheDefaultToBeSupported() =>
        Should.Throw<InvalidOperationException>(() => new SupportedLocales(["es-AR"], "en"));
}
