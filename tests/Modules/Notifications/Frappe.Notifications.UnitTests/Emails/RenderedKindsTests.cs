using System.Text.RegularExpressions;
using Frappe.Notifications.Channels.Email;
using Frappe.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Frappe.Notifications.UnitTests.Emails;

/// <summary>Every kind's sample renders in every language, to the design of S7, and the output stays as the goldens say.</summary>
public sealed partial class RenderedKindsTests
{
    private static readonly string[] Kinds =
        ["account_activation_code", "password_reset_code", "email_change_code", "product_announcement"];

    private static readonly string[] Languages = ["es-AR", "es", "en", "pt-BR"];

    public static IEnumerable<(string Kind, string Language)> Everything() =>
        Kinds.SelectMany(kind => Languages.Select(language => (kind, language)));

    public static IEnumerable<(string Kind, string Language)> Codes() => Everything().Where(pair => pair.Kind != "product_announcement");

    [GeneratedRegex("#[0-9a-fA-F]{3,8}\\b", RegexOptions.None, 1000)]
    private static partial Regex HexColors();

    [GeneratedRegex("\\b(rgba?|hsla?|hwb|lab|lch|oklab|oklch|color)\\(", RegexOptions.IgnoreCase, 1000)]
    private static partial Regex ColorFunctions();

    [GeneratedRegex("(?:^|[;\"])\\s*(?:color|background|background-color|border(?:-[a-z]+)*)\\s*:\\s*([^;\"]*)", RegexOptions.IgnoreCase, 1000)]
    private static partial Regex ColorDeclarations();

    [GeneratedRegex("[a-z]+", RegexOptions.IgnoreCase, 1000)]
    private static partial Regex Words();

    [GeneratedRegex("border:2px solid", RegexOptions.None, 1000)]
    private static partial Regex CodeBoxes();

    private static string GoldenPath(string kind, string language, string extension) => Path.Combine(
        RepositoryRoot.FullPath,
        "tests",
        "Modules",
        "Notifications",
        "Frappe.Notifications.UnitTests",
        "Emails",
        "Fixtures",
        "Golden",
        $"{kind}.{language}.{extension}");

    private static async Task<RenderedEmail> SendAsync(EmailWorld world, string kind, string language)
    {
        var outcome = await world.Channel.DeliverAsync(world.ItemOf(kind, language), CancellationToken.None);
        await Assert.That(outcome).IsTypeOf<Frappe.Notifications.Application.Channels.ChannelOutcome.Sent>();
        return world.Sender.Sent[^1];
    }

    [Test]
    [MethodDataSource(nameof(Everything))]
    public async Task Every_sample_renders_in_every_language(string kind, string language)
    {
        // Arrange
        await using var world = new EmailWorld();

        // Act
        var email = await SendAsync(world, kind, language);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(email.Language.Value).IsEqualTo(language);
            await Assert.That(email.Subject).IsNotEmpty();
            await Assert.That(email.Html).Contains($"<html lang=\"{language}\">");
            await Assert.That(email.Text).IsNotEmpty();
            await Assert.That(email.To.Value).IsEqualTo("ana@example.com");
            await Assert.That(email.IdempotencyKey).IsEqualTo("idem-1");
        }
    }

    [Test]
    [MethodDataSource(nameof(Codes))]
    public async Task A_security_mail_has_the_code_in_six_boxes_and_in_the_subject_and_no_link(string kind, string language)
    {
        // Arrange
        await using var world = new EmailWorld();

        // Act
        var email = await SendAsync(world, kind, language);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(email.Subject).Contains("482913");
            await Assert.That(CodeBoxes().Count(email.Html)).IsEqualTo(6);
            await Assert.That(email.Html).Contains("width:84px;height:84px");
            await Assert.That(email.Html).DoesNotContain("<a ");
            await Assert.That(email.Html).DoesNotContain("href");
            await Assert.That(email.Text).DoesNotContain("http");
            await Assert.That(email.Headers).IsEmpty();
        }
    }

    [Test]
    [MethodDataSource(nameof(Everything))]
    public async Task The_mail_uses_black_and_white_only(string kind, string language)
    {
        // Arrange
        await using var world = new EmailWorld();

        // Act
        var email = await SendAsync(world, kind, language);

        // Assert
        await Assert.That(HexColors().Matches(email.Html).Select(match => match.Value.ToLowerInvariant()).Distinct())
            .IsEquivalentTo(["#000000", "#ffffff"]);
        await Assert.That(ColorFunctions().IsMatch(email.Html)).IsFalse();
        var namedColors = ColorDeclarations().Matches(email.Html)
            .SelectMany(match => Words().Matches(HexColors().Replace(match.Groups[1].Value, string.Empty)).Select(word => word.Value.ToLowerInvariant()))
            .Where(word => word is not ("black" or "white" or "transparent" or "solid" or "none" or "px" or "separate" or "collapse"))
            .Distinct();
        await Assert.That(namedColors).IsEmpty();
    }

    [Test]
    [MethodDataSource(nameof(Everything))]
    public async Task The_output_matches_its_golden_files(string kind, string language)
    {
        // Arrange
        await using var world = new EmailWorld();
        var email = await SendAsync(world, kind, language);
        var html = GoldenPath(kind, language, "html");
        var text = GoldenPath(kind, language, "txt");
        if (Environment.GetEnvironmentVariable("FRAPPE_UPDATE_GOLDEN") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(html)!);
            await File.WriteAllTextAsync(html, email.Html);
            await File.WriteAllTextAsync(text, email.Text);
        }

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(File.Exists(html)).IsTrue();
            await Assert.That(File.Exists(text)).IsTrue();
            await Assert.That(email.Html).IsEqualTo(await File.ReadAllTextAsync(html));
            await Assert.That(email.Text).IsEqualTo(await File.ReadAllTextAsync(text));
        }
    }

    [Test]
    public async Task The_same_delivery_renders_to_the_same_bytes_every_time()
    {
        // Arrange
        await using var world = new EmailWorld();

        // Act
        var first = await SendAsync(world, "account_activation_code", "es-AR");
        var second = await SendAsync(world, "account_activation_code", "es-AR");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(second.Html).IsEqualTo(first.Html);
            await Assert.That(second.Text).IsEqualTo(first.Text);
            await Assert.That(second.Subject).IsEqualTo(first.Subject);
        }
    }

    [Test]
    public async Task The_activation_mail_says_what_the_design_says()
    {
        // Arrange
        await using var world = new EmailWorld();

        // Act
        var email = await SendAsync(world, "account_activation_code", "es-AR");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(email.Subject).IsEqualTo("Tu código de Frappé: 482913");
            await Assert.That(email.Text).Contains("Activá tu cuenta");
            await Assert.That(email.Text).Contains("Vence en 15 minutos · Un solo uso");
            await Assert.That(email.Text).Contains("Si no creaste una cuenta en Frappé, ignorá este mail: sin el código nadie puede usarla.");
        }
    }

    [Test]
    [Arguments("password_reset_code", "Cambiá tu contraseña", "¿No lo pediste? Tu contraseña sigue igual y podés ignorar este mail.")]
    [Arguments("email_change_code", "Confirmá tu email nuevo", "Si no pediste este cambio, ignorá este mail y no pasa nada.")]
    public async Task The_other_security_mails_say_what_the_design_says(string kind, string heading, string footer)
    {
        // Arrange
        await using var world = new EmailWorld();

        // Act
        var email = await SendAsync(world, kind, "es-AR");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(email.Text).Contains(heading);
            await Assert.That(email.Text).Contains(footer);
        }
    }

    [Test]
    public async Task A_marketing_mail_gets_the_visible_link_and_the_one_click_headers()
    {
        // Arrange
        await using var world = new EmailWorld();

        // Act
        var email = await SendAsync(world, "product_announcement", "es-AR");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(email.Html).Contains("href=\"https://frappe.example/unsubscribe?c=marketing\"");
            await Assert.That(email.Html).Contains("Dejar de recibir estos mails");
            await Assert.That(email.Text).Contains("Dejar de recibir estos mails: https://frappe.example/unsubscribe?c=marketing");
            await Assert.That(email.Headers["List-Unsubscribe"])
                .IsEqualTo("<https://api.frappe.example/v1/notifications/unsubscriptions?token=t0k3n>");
            await Assert.That(email.Headers["List-Unsubscribe-Post"]).IsEqualTo("List-Unsubscribe=One-Click");
            await Assert.That(email.Html).Contains("href=\"https://frappe.example/news\"");
        }
    }

    [Test]
    public async Task A_marketing_mail_is_never_rendered_without_a_way_to_unsubscribe()
    {
        // Arrange
        await using var world = new EmailWorld(services => services
            .RemoveAll<IEmailUnsubscribeLinks>()
            .AddSingleton<IEmailUnsubscribeLinks, UnconfiguredUnsubscribeLinks>());

        // Act
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await world.Channel.DeliverAsync(world.ItemOf("product_announcement"), CancellationToken.None));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(thrown!.Message).Contains("IEmailUnsubscribeLinks");
            await Assert.That(world.Sender.Sent).IsEmpty();
        }
    }
}
