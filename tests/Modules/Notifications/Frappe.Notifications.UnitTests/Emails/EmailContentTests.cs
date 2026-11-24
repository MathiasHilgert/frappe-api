using Frappe.Notifications.Channels.Email;

namespace Frappe.Notifications.UnitTests.Emails;

/// <summary>The closed blocks and the two builders: a Security mail has no way to say "link" (S7).</summary>
public sealed class EmailContentTests
{
    [Test]
    public async Task A_secure_email_builder_has_no_action_method()
    {
        // Act
        var actionMethods = typeof(SecureEmail).GetMethods().Where(method => method.Name == "Action").ToArray();

        // Assert
        await Assert.That(actionMethods).IsEmpty();
    }

    [Test]
    public async Task A_standard_email_builder_adds_an_action_block()
    {
        // Act
        var email = Email.Standard("Novedades").Heading("Hola").Action("Ver novedades", "https://frappe.example/news");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(email.HasAction).IsTrue();
            await Assert.That(email.Blocks[^1]).IsEqualTo(new EmailBlock.Action("Ver novedades", new Uri("https://frappe.example/news")));
        }
    }

    [Test]
    public async Task The_blocks_keep_the_order_they_were_added_in()
    {
        // Act
        var email = Email.Secure("Tu código: 482913").Preheader("Vence en 15 minutos").Heading("Activá tu cuenta").Paragraph("Hola Ana")
            .Code("482913").Meta("Vence en 15 minutos", "Un solo uso").Footer("Ignorá este mail");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(email.Subject).IsEqualTo("Tu código: 482913");
            await Assert.That(email.PreheaderText).IsEqualTo("Vence en 15 minutos");
            await Assert.That(email.HasAction).IsFalse();
            await Assert.That(email.Blocks.Select(block => block.GetType().Name))
                .IsEquivalentTo(["Heading", "Paragraph", "Code", "Meta", "Footer"]);
        }
    }

    [Test]
    [Arguments("")]
    [Arguments("   ")]
    [Arguments("two\r\nlines")]
    public async Task A_subject_is_one_non_blank_line(string subject)
    {
        // Act
        var thrown = Assert.Throws<ArgumentException>(() => Email.Secure(subject));

        // Assert
        await Assert.That(thrown).IsNotNull();
    }

    [Test]
    [Arguments("")]
    [Arguments("12 34")]
    [Arguments("1234567890123")]
    public async Task A_code_is_one_to_twelve_characters_without_spaces(string code)
    {
        // Act
        var thrown = Assert.Throws<ArgumentException>(() => Email.Secure("s").Code(code));

        // Assert
        await Assert.That(thrown).IsNotNull();
    }

    [Test]
    [Arguments("not a url")]
    [Arguments("/relative")]
    [Arguments("javascript:alert(1)")]
    [Arguments("http://frappe.example/insecure")]
    public async Task An_action_takes_an_absolute_https_url(string url)
    {
        // Act
        var thrown = Assert.Throws<ArgumentException>(() => Email.Standard("s").Action("Ir", url));

        // Assert
        await Assert.That(thrown).IsNotNull();
    }

    [Test]
    public async Task The_draft_and_its_blocks_print_no_content()
    {
        // Arrange
        var email = Email.Secure("Tu código: 482913").Heading("Hola Ana").Code("482913");

        // Act
        var printed = $"{email} {string.Join(' ', email.Blocks)}";

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(printed).DoesNotContain("482913");
            await Assert.That(printed).DoesNotContain("Ana");
        }
    }

    [Test]
    [Arguments("  Ana@Example.COM ", "ana@example.com")]
    [Arguments("ANA@EXAMPLE.COM", "ana@example.com")]
    [Arguments("josé@example.com", "josé@example.com")]
    public async Task An_address_is_trimmed_lower_cased_and_nfc_normalized(string raw, string expected)
    {
        // Act
        var result = EmailAddress.Create(raw);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.IsSuccess).IsTrue();
            await Assert.That(result.Value.Value).IsEqualTo(expected);
        }
    }

    [Test]
    [Arguments("")]
    [Arguments("ana")]
    [Arguments("ana@")]
    [Arguments("@example.com")]
    [Arguments("a b@example.com")]
    [Arguments("ana@@example.com")]
    [Arguments("ana@example.com,bob@example.com")]
    public async Task A_malformed_address_is_refused(string raw)
    {
        // Act
        var result = EmailAddress.Create(raw);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.IsSuccess).IsFalse();
            await Assert.That(result.Errors[0].Code).IsEqualTo("notifications.email_address.invalid_format");
        }
    }
}
