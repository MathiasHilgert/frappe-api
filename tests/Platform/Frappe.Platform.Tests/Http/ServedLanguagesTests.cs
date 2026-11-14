using Frappe.Http;
using Frappe.Localization;
using Microsoft.AspNetCore.Http;

namespace Frappe.Platform.Tests.Http;

public sealed class ServedLanguagesTests
{
    [Test]
    public async Task Content_Language_lists_each_served_language_once_in_order_and_skips_identifiers()
    {
        var context = new DefaultHttpContext();

        ServedLanguages.Declare(context, [LanguageTag.Create("pt-BR").Value, null, LanguageTag.Create("es").Value, LanguageTag.Create("es").Value]);

        await Assert.That(context.Response.Headers.ContentLanguage.ToString()).IsEqualTo("es, pt-BR");
    }

    [Test]
    public async Task Nothing_served_in_a_language_declares_nothing()
    {
        var context = new DefaultHttpContext();

        ServedLanguages.Declare(context, [null]);

        await Assert.That(context.Response.Headers.ContainsKey("Content-Language")).IsFalse();
    }

    [Test]
    public async Task An_endpoint_outside_a_negotiating_group_is_a_wiring_error() =>
        await Assert.That(() => ServedLanguages.LanguageOf(new DefaultHttpContext())).Throws<InvalidOperationException>();
}
