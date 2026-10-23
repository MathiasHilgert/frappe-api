using Frappe.SharedKernel.Localization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Frappe.Platform.Localization.Tests;

public sealed class MessageCatalogTests
{
    private static IMessageCatalog Catalog(params (string Key, string Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();
        return new ServiceCollection()
            .AddPlatformLocalization(configuration)
            .AddMessageCatalog(typeof(MessageCatalogTests).Assembly)
            .BuildServiceProvider()
            .GetRequiredService<IMessageCatalog>();
    }

    private static Dictionary<string, object?> Args(string name, object value) => new(StringComparer.Ordinal) { [name] = value };

    [Theory]
    [InlineData("en", "Hello, Ana!")]
    [InlineData("es-AR", "¡Hola, Ana!")]
    [InlineData("pt-BR", "Olá, Ana!")]
    public void Format_UsesTheLocaleCatalogue(string locale, string expected) =>
        Catalog().Format(Locale.Parse(locale), "sample.greeting", Args("name", "Ana")).ShouldBe(expected);

    [Theory]
    [InlineData(1, "Tenés 1 código")]
    [InlineData(3, "Tenés 3 códigos")]
    public void Format_Pluralises(int count, string expected) =>
        Catalog().Format(Locale.Parse("es-AR"), "sample.codes", Args("count", count)).ShouldBe(expected);

    [Fact]
    public void Format_MissingLocale_FallsBackToTheSameLanguage() =>
        Catalog(("Localization:SupportedLocales:0", "es-AR"), ("Localization:SupportedLocales:1", "es-CL"),
                ("Localization:SupportedLocales:2", "en"))
            .Format(Locale.Parse("es-CL"), "sample.greeting", Args("name", "Ana")).ShouldBe("¡Hola, Ana!");

    [Fact]
    public void Format_UnknownLocale_FallsBackToEnglish() =>
        Catalog().Format(Locale.Parse("de"), "sample.only-in-en").ShouldBe("Only in English");

    [Fact]
    public void Format_KeyMissingFromEnglish_Throws() =>
        Should.Throw<KeyNotFoundException>(() => Catalog().Format(Locale.Parse("es-AR"), "sample.nope"));

    [Fact]
    public void DuplicateKeysAcrossCatalogues_FailAtStartup() =>
        Should.Throw<InvalidOperationException>(() => new ServiceCollection()
            .AddPlatformLocalization(new ConfigurationBuilder().Build())
            .AddMessageCatalog(typeof(MessageCatalogTests).Assembly)
            .AddMessageCatalog(typeof(MessageCatalogTests).Assembly)
            .BuildServiceProvider()
            .GetRequiredService<IMessageCatalog>());

    [Fact]
    public void TryFormat_UnknownKey_IsNull() =>
        Catalog().TryFormat(Locale.Parse("es-AR"), "sample.nope").ShouldBeNull();
}
