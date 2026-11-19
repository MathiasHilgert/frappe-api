using Frappe.Http;
using Frappe.Localization;
using Microsoft.AspNetCore.Http;

namespace Frappe.Platform.UnitTests.Http;

/// <summary>The languages a request is answered in and the one it negotiated.</summary>
public sealed class ServedLanguagesTests
{
    [Test]
    public async Task Content_Language_lists_each_served_language_once_in_order_and_skips_identifiers()
    {
        // Arrange
        var context = new DefaultHttpContext();

        // Act
        ServedLanguages.Declare(context, [LanguageTag.Create("pt-BR").Value, null, LanguageTag.Create("es").Value, LanguageTag.Create("es").Value]);

        // Assert
        await Assert.That(context.Response.Headers.ContentLanguage.ToString()).IsEqualTo("es, pt-BR");
    }

    [Test]
    public async Task Languages_declared_by_separate_calls_accumulate_in_one_header()
    {
        // Arrange
        var context = new DefaultHttpContext();

        // Act
        ServedLanguages.Declare(context, [LanguageTag.Create("pt-BR").Value]);
        ServedLanguages.Declare(context, [LanguageTag.Create("es").Value]);

        // Assert
        await Assert.That(context.Response.Headers.ContentLanguage.ToString()).IsEqualTo("es, pt-BR");
    }

    [Test]
    public async Task The_negotiated_language_is_the_one_the_endpoint_answers_in()
    {
        // Arrange
        var context = new DefaultHttpContext();
        var tag = LanguageTag.Create("es").Value;
        context.Features.Set(new RequestLanguage(tag));

        // Act
        var language = ServedLanguages.LanguageOf(context);

        // Assert
        await Assert.That(language.Value).IsEqualTo("es");
    }

    [Test]
    public async Task Nothing_served_in_a_language_declares_nothing()
    {
        // Arrange
        var context = new DefaultHttpContext();

        // Act
        ServedLanguages.Declare(context, [null]);

        // Assert
        await Assert.That(context.Response.Headers.ContainsKey("Content-Language")).IsFalse();
    }

    [Test]
    public async Task An_endpoint_outside_a_negotiating_group_is_a_wiring_error()
    {
        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => ServedLanguages.LanguageOf(new DefaultHttpContext()));

        // Assert
        await Assert.That(failure).IsNotNull();
    }
}
