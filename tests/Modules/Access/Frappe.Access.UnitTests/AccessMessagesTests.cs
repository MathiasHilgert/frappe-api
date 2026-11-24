using Frappe.Access.Contracts;
using Frappe.Access.Http;
using Frappe.Http;
using Frappe.Localization;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Access.UnitTests;

/// <summary>The texts of the <c>access.*</c> errors exist in every served language.</summary>
public sealed class AccessMessagesTests
{
    [Test]
    [Arguments(AccessErrorCodes.ActionForbidden, "en")]
    [Arguments(AccessErrorCodes.ActionForbidden, "es")]
    [Arguments(AccessErrorCodes.ActionForbidden, "es-AR")]
    [Arguments(AccessErrorCodes.ActionForbidden, "pt-BR")]
    [Arguments(AccessErrorCodes.StepUpRequired, "en")]
    [Arguments(AccessErrorCodes.StepUpRequired, "es")]
    [Arguments(AccessErrorCodes.StepUpRequired, "es-AR")]
    [Arguments(AccessErrorCodes.StepUpRequired, "pt-BR")]
    public async Task Each_access_code_renders_a_text_in_each_language(string code, string language)
    {
        var services = new ServiceCollection();
        new AccessHttp().AddServices(services);
        using var provider = services.BuildServiceProvider();

        var rendered = provider.GetRequiredService<ErrorMessageCatalog>()
            .Render(code, LanguageTag.Create(language).Value, new Dictionary<string, object?>());

        await Assert.That(rendered).IsNotNull();
        await Assert.That(string.IsNullOrWhiteSpace(rendered!.Value.Text)).IsFalse();
        await Assert.That(rendered.Value.Language.Value).IsEqualTo(language);
    }
}
