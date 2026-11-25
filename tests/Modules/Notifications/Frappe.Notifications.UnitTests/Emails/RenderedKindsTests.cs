using System.Text.RegularExpressions;
using Frappe.Notifications.Messages.Account;
using Frappe.Notifications.Senders.Email;
using Frappe.Testing;

namespace Frappe.Notifications.UnitTests.Emails;

/// <summary>Every kind's sample renders in every language, to the design of S7, and the output stays as the goldens say.</summary>
public sealed partial class RenderedKindsTests
{
    private static readonly string[] Kinds =
    [
        "account_activation_code", "password_reset_code", "email_change_code", "registration_attempted", "email_change_requested",
        "email_changed", "product_announcement",
    ];

    /// <summary>Every color of the design system's light and dark tokens an email may use.</summary>
    private static readonly string[] Tokens =
        ["#f5f2f3", "#151013", "#ffffff", "#201a1d", "#1f1418", "#f4eef0", "#665860", "#b8aab1", "#e3dbe0", "#3a3036", "#6b1730", "#f2c4d1"];

    private static readonly string[] Languages = ["es-AR", "es", "en", "pt-BR"];

    public static IEnumerable<(string Kind, string Language)> Everything() =>
        Kinds.SelectMany(kind => Languages.Select(language => (kind, language)));

    public static IEnumerable<(string Kind, string Language)> Codes() =>
        Everything().Where(pair => pair.Kind is "account_activation_code" or "password_reset_code" or "email_change_code");

    public static IEnumerable<(string Kind, string Language)> Notices() =>
        Everything().Where(pair => pair.Kind is "registration_attempted" or "email_change_requested");

    [GeneratedRegex("#[0-9a-fA-F]{3,8}\\b", RegexOptions.None, 1000)]
    private static partial Regex HexColors();

    [GeneratedRegex("\\b(gradient|hsla?|hwb|lab|lch|oklab|oklch|color)\\(", RegexOptions.IgnoreCase, 1000)]
    private static partial Regex UnwantedColorFunctions();

    [GeneratedRegex("href=\"[^\"]*", RegexOptions.None, 1000)]
    private static partial Regex Hrefs();

    [GeneratedRegex("text-align:center", RegexOptions.None, 1000)]
    private static partial Regex CenteredText();

    [GeneratedRegex("border:2px solid", RegexOptions.None, 1000)]
    private static partial Regex CodeBoxes();

    [GeneratedRegex("<td class=\"box\"[^>]*>482913</td>", RegexOptions.None, 1000)]
    private static partial Regex CodeInBox();

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
    public async Task A_security_mail_has_the_code_in_one_copyable_box_and_in_the_subject_and_no_link(string kind, string language)
    {
        // Arrange
        await using var world = new EmailWorld();

        // Act
        var email = await SendAsync(world, kind, language);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(email.Subject).Contains("482913");
            await Assert.That(CodeBoxes().Count(email.Html)).IsEqualTo(1);
            await Assert.That(CodeInBox().Count(email.Html)).IsEqualTo(1);
            await Assert.That(email.Html).Contains("<table role=\"presentation\" align=\"left\" cellpadding=\"0\" cellspacing=\"0\">");
            await Assert.That(email.Html).Contains("class=\"box\"").And.Contains("user-select:all").And.Contains("letter-spacing:0.35em");
            await Assert.That(email.Text).Contains("\n482913\n");
            await Assert.That(email.Html).Contains("width=\"56\" height=\"56\"");
            await Assert.That(email.Html).DoesNotContain("<a ");
            await Assert.That(Hrefs().Matches(email.Html).Select(match => match.Value))
                .All().Satisfy(href => href.StartsWith("href=\"https://fonts.g", StringComparison.Ordinal));
            await Assert.That(email.Text).DoesNotContain("http");
            await Assert.That(email.Headers).IsEmpty();
        }
    }

    [Test]
    [MethodDataSource(nameof(Everything))]
    public async Task The_mail_uses_only_the_frappe_color_tokens_and_no_gradients(string kind, string language)
    {
        // Arrange
        await using var world = new EmailWorld();

        // Act
        var email = await SendAsync(world, kind, language);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(HexColors().Matches(email.Html).Select(match => match.Value.ToLowerInvariant()).Distinct().Except(Tokens))
                .IsEmpty();
            await Assert.That(UnwantedColorFunctions().IsMatch(email.Html)).IsFalse();
            await Assert.That(email.Html.Contains("#6b1730", StringComparison.Ordinal)).IsEqualTo(kind is "product_announcement" or "email_changed");
        }
    }

    [Test]
    [MethodDataSource(nameof(Everything))]
    public async Task The_mail_follows_the_light_and_the_dark_scheme_with_the_two_official_logos(string kind, string language)
    {
        // Arrange
        await using var world = new EmailWorld();

        // Act
        var email = await SendAsync(world, kind, language);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(email.Html).Contains("<meta name=\"color-scheme\" content=\"light dark\">");
            await Assert.That(email.Html).Contains("<meta name=\"supported-color-schemes\" content=\"light dark\">");
            await Assert.That(email.Html).Contains("@media (prefers-color-scheme:dark)");
            await Assert.That(email.Html).Contains("[data-ogsc]").And.Contains("[data-ogsb]");
            await Assert.That(email.Html).Contains("src=\"cid:" + EmailInlineImage.LightLogoId + "\"")
                .And.Contains("src=\"cid:" + EmailInlineImage.DarkLogoId + "\"");
            await Assert.That(email.Html).Contains("display:none;mso-hide:all");
        }
    }

    [Test]
    [MethodDataSource(nameof(Everything))]
    public async Task The_elements_the_dark_rules_target_carry_their_classes(string kind, string language)
    {
        // Arrange
        await using var world = new EmailWorld();

        // Act
        var email = await SendAsync(world, kind, language);

        // Assert
        var isAnnouncement = kind is "product_announcement" or "email_changed";
        var hasCode = kind.EndsWith("_code", StringComparison.Ordinal);
        using (Assert.Multiple())
        {
            foreach (var name in new[] { "bg-ground", "bg-surface", "ink", "muted" })
            {
                await Assert.That(email.Html).Contains(name);
            }

            await Assert.That(email.Html.Contains("class=\"btn\"", StringComparison.Ordinal)).IsEqualTo(isAnnouncement);
            await Assert.That(email.Html.Contains("class=\"btn-text\"", StringComparison.Ordinal)).IsEqualTo(isAnnouncement);
            await Assert.That(email.Html.Contains("class=\"box\"", StringComparison.Ordinal)).IsEqualTo(hasCode);
        }
    }

    [Test]
    [MethodDataSource(nameof(Everything))]
    public async Task The_mail_uses_the_brand_fonts_and_keeps_everything_left_aligned(string kind, string language)
    {
        // Arrange
        await using var world = new EmailWorld();

        // Act
        var email = await SendAsync(world, kind, language);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(email.Html).Contains("family=Bricolage+Grotesque").And.Contains("family=Hanken+Grotesk");
            await Assert.That(email.Html).Contains("font-family:Bricolage Grotesque,system-ui");
            await Assert.That(email.Html).Contains("font-family:Hanken Grotesk,system-ui");
            await Assert.That(email.Html).DoesNotContain("Roboto").And.DoesNotContain("Inter").And.DoesNotContain("Poppins");
            await Assert.That(CenteredText().Count(email.Html)).IsEqualTo(0);
        }
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
    [MethodDataSource(nameof(Notices))]
    public async Task A_security_notice_has_no_code_no_link_and_no_unsubscribe(string kind, string language)
    {
        // Arrange
        await using var world = new EmailWorld();

        // Act
        var email = await SendAsync(world, kind, language);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(email.Subject).IsNotEmpty().And.DoesNotContain("482913");
            await Assert.That(CodeBoxes().Count(email.Html)).IsEqualTo(0);
            await Assert.That(email.Html).DoesNotContain("<a ").And.DoesNotContain("class=\"box\"");
            await Assert.That(email.Text).DoesNotContain("http");
            await Assert.That(email.Headers).IsEmpty();
        }
    }

    [Test]
    public async Task The_registration_attempt_mail_says_what_the_design_says()
    {
        // Arrange
        await using var world = new EmailWorld();

        // Act
        var email = await SendAsync(world, "registration_attempted", "es-AR");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(email.Subject).IsEqualTo("Alguien intentó crear una cuenta de Frappé con tu email");
            await Assert.That(email.Text).Contains("Si fuiste vos, iniciá sesión o restablecé tu contraseña.");
            await Assert.That(email.Text).Contains("Si no fuiste vos, ignorá este mail: tu cuenta y tu contraseña siguen igual.");
        }
    }

    [Test]
    public async Task The_email_change_mail_names_the_masked_new_address_and_says_to_change_the_password_if_it_was_not_the_owner()
    {
        // Arrange
        await using var world = new EmailWorld();

        // Act
        var email = await SendAsync(world, "email_change_requested", "es-AR");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(email.To.Value).IsEqualTo("ana@example.com");
            await Assert.That(email.Text).Contains("Hola Ana. Se pidió cambiar el email de tu cuenta a n***@example.com.");
            await Assert.That(email.Text).Contains("Si no fuiste vos, cambiá tu contraseña ahora");
        }
    }

    [Test]
    public async Task The_email_changed_mail_has_one_undo_button_to_the_console_and_names_no_new_address()
    {
        // Arrange
        await using var world = new EmailWorld();

        // Act
        var email = await SendAsync(world, "email_changed", "es-AR");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(email.To.Value).IsEqualTo("ana@example.com");
            await Assert.That(email.Subject).IsEqualTo("Se cambió tu email de Frappé");
            await Assert.That(email.Html).Contains("href=\"https://console.example.com/undo-email-change?token=sample\"");
            await Assert.That(email.Text).Contains("Deshacer el cambio");
        }
    }

    [Test]
    [Arguments("https://console.example.com/undo-email-change?token=t", true)]
    [Arguments("http://console.example.com/undo-email-change?token=t", false)]
    [Arguments("undo", false)]
    public async Task The_undo_url_must_be_an_absolute_https_url_and_the_payload_prints_nothing_private(string url, bool valid)
    {
        // Arrange
        var payload = new EmailChangedPayload("ana@example.com", "es-AR", "Ana", url);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(payload.InvalidFields().Count == 0).IsEqualTo(valid);
            await Assert.That(payload.ToString()).DoesNotContain("ana@example.com").And.DoesNotContain("token").And.DoesNotContain("Ana ");
        }
    }

    [Test]
    [Arguments("", false)]
    [Arguments("no-at-sign", false)]
    [Arguments("n***@exa mple.com", false)]
    [Arguments("n***@https://example.com", false)]
    [Arguments("n***@example.com", true)]
    public async Task The_masked_address_must_look_like_one_and_carry_no_link(string masked, bool valid)
    {
        // Arrange
        var payload = new EmailChangeRequestedPayload("ana@example.com", "es-AR", "Ana", masked);

        // Act
        var invalid = payload.InvalidFields();

        // Assert
        await Assert.That(invalid.Count == 0).IsEqualTo(valid);
    }

    [Test]
    public async Task The_notice_payloads_print_no_personal_data()
    {
        // Act
        var printed = new RegistrationAttemptedPayload("ana@example.com", "es-AR") + " "
            + new EmailChangeRequestedPayload("ana@example.com", "es-AR", "Ana", "n***@example.com");

        // Assert
        await Assert.That(printed).DoesNotContain("ana@").And.DoesNotContain("Ana ").And.DoesNotContain("example.com");
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
            await Assert.That(email.Text).Contains("Te escribimos porque tenés una cuenta en Frappé.");
            await Assert.That(email.Html).Contains(">Dejar de recibir novedades</a>");
            await Assert.That(email.Html).Contains("Ver novedades");
            await Assert.That(email.Text).Contains("Dejar de recibir novedades: https://frappe.example/unsubscribe?c=marketing");
            await Assert.That(email.Headers["List-Unsubscribe"])
                .IsEqualTo("<https://api.frappe.example/v1/notifications/unsubscriptions?token=t0k3n>");
            await Assert.That(email.Headers["List-Unsubscribe-Post"]).IsEqualTo("List-Unsubscribe=One-Click");
            await Assert.That(email.Html).Contains("href=\"https://frappe.example/news\"");
        }
    }
}
