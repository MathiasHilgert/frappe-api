using System.Text.RegularExpressions;
using Frappe.Notification.Application.OwnerCodeMails;
using Frappe.Notification.Infrastructure.Mails;
using Frappe.Platform.Localization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Frappe.Notification.Tests;

public sealed partial class OwnerCodeMailsTests
{
    private static readonly IOwnerCodeMails Mails = new ServiceCollection()
        .AddSingleton<IConfiguration>(new ConfigurationBuilder().Build()).AddPlatformLocalization()
        .AddMessageCatalog(typeof(CatalogOwnerCodeMails).Assembly)
        .AddSingleton<IOwnerCodeMails, CatalogOwnerCodeMails>()
        .BuildServiceProvider()
        .GetRequiredService<IOwnerCodeMails>();

    public static TheoryData<OwnerCodeMailKind, string?, string> Subjects => new()
    {
        { OwnerCodeMailKind.Verification, "en", "Your Frappé code: 482913" },
        { OwnerCodeMailKind.Verification, "es-AR", "Tu código de Frappé: 482913" },
        { OwnerCodeMailKind.Verification, "es-MX", "Tu código de Frappé: 482913" },
        { OwnerCodeMailKind.Verification, "pt-BR", "Seu código do Frappé: 482913" },
        { OwnerCodeMailKind.PasswordReset, "es-AR", "Código para cambiar tu contraseña: 482913" },
        { OwnerCodeMailKind.EmailChange, "pt-BR", "Código para confirmar seu novo email: 482913" },
        { OwnerCodeMailKind.Verification, null, "Your Frappé code: 482913" },
        { OwnerCodeMailKind.Verification, "de", "Your Frappé code: 482913" },
        { OwnerCodeMailKind.Verification, "not a tag!", "Your Frappé code: 482913" },
        { OwnerCodeMailKind.Verification, "es-CL", "Tu código de Frappé: 482913" },
    };

    [Fact]
    public void Catalogue_HasEveryKeyInEveryLocale_AndEveryMessageFormats() =>
        MessageCatalogConformance.Check(typeof(CatalogOwnerCodeMails).Assembly).ShouldBeEmpty();

    [Theory]
    [MemberData(nameof(Subjects))]
    public void Subject_CarriesTheCode_InTheOwnersLanguage_FallingBackToEnglish(OwnerCodeMailKind kind, string? locale, string subject) =>
        Mails.Compose(kind, "ana@example.test", "Ana", "482913", locale).Subject.ShouldBe(subject);

    [Theory]
    [InlineData(OwnerCodeMailKind.Verification)]
    [InlineData(OwnerCodeMailKind.PasswordReset)]
    [InlineData(OwnerCodeMailKind.EmailChange)]
    public void Html_ShowsTheLogoAndSixDigitBoxes_WithNoLinks(OwnerCodeMailKind kind)
    {
        var mail = Mails.Compose(kind, "ana@example.test", "Ana", "482913", "en");

        mail.To.ShouldBe("ana@example.test");
        var html = mail.Html.ShouldNotBeNull();
        DigitCell().Matches(html).Select(m => m.Groups["d"].Value).ShouldBe(["4", "8", "2", "9", "1", "3"]);
        html.ShouldContain("src=\"cid:frappe-logo\"");
        html.ShouldContain("width=\"84\"");
        html.ShouldNotContain("<a ", Case.Insensitive);
        html.ShouldNotContain("http", Case.Insensitive);
        html.ShouldContain("<html lang=\"en\"");
        mail.InlineImages.Select(i => i.ContentId).ShouldBe(["frappe-logo", "frappe-logo-dark"]);
        mail.InlineImages.ShouldAllBe(i => i.ContentType == "image/png" && i.Content.Length > 0);
    }

    [Fact]
    public void Text_IsAPlainVersionOfTheSameMail()
    {
        var mail = Mails.Compose(OwnerCodeMailKind.Verification, "ana@example.test", "Ana", "482913", "es-AR");

        mail.Text.ShouldContain("Activá tu cuenta");
        mail.Text.ShouldContain("Hola Ana.");
        mail.Text.ShouldContain("482913");
        mail.Text.ShouldContain("Vence en 15 minutos");
        mail.Text.ShouldNotContain("<");
    }

    [Fact]
    public void Html_EncodesTheOwnersName()
    {
        var mail = Mails.Compose(OwnerCodeMailKind.Verification, "ana@example.test", "<b>Ana</b> & co", "482913", "en");

        mail.Html.ShouldNotBeNull().ShouldContain("&lt;b&gt;Ana&lt;/b&gt; &amp; co");
        mail.Html.ShouldNotContain("<b>Ana</b>");
    }

    [GeneratedRegex("<td class=\"digit\"[^>]*>(?<d>\\d)</td>", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex DigitCell();
}
