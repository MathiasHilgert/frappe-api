package com.frappe.platform.infrastructure.mail;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatIllegalArgumentException;
import static org.assertj.core.api.Assertions.assertThatIllegalStateException;

import com.frappe.platform.mail.MailMessage;
import gg.jte.ContentType;
import gg.jte.TemplateEngine;
import io.micrometer.core.instrument.simple.SimpleMeterRegistry;
import java.util.Locale;
import java.util.Map;
import org.junit.jupiter.api.Test;
import org.springframework.context.support.StaticMessageSource;

class MailRendererTest {

    private static final Locale ENGLISH = Locale.of("en");

    private static final Locale SPANISH = Locale.of("es");

    private static final Locale PORTUGUESE = Locale.of("pt");

    private final SimpleMeterRegistry registry = new SimpleMeterRegistry();

    private final StaticMessageSource catalogs = new StaticMessageSource();

    private final MailRenderer renderer =
            new MailRenderer(TemplateEngine.createPrecompiled(ContentType.Html), catalogs, registry);

    MailRendererTest() {
        catalogs.addMessage("mailprobe.mail.welcome.subject", ENGLISH, "Your Frappé code");
        catalogs.addMessage("mailprobe.mail.welcome.greeting", ENGLISH, "Hello, {0}!");
        catalogs.addMessage("mailprobe.mail.welcome.code", ENGLISH, "Your code is {0}.");
        catalogs.addMessage("mailprobe.mail.welcome.subject", SPANISH, "Tu código de Frappé");
        catalogs.addMessage("mailprobe.mail.welcome.greeting", SPANISH, "¡Hola, {0}!");
        catalogs.addMessage("mailprobe.mail.welcome.code", SPANISH, "Tu código es {0}.");
        // Portuguese lacks the code line: a half-translated template.
        catalogs.addMessage("mailprobe.mail.welcome.subject", PORTUGUESE, "Seu código do Frappé");
        catalogs.addMessage("mailprobe.mail.welcome.greeting", PORTUGUESE, "Olá, {0}!");
    }

    @Test
    void rendersACompleteTemplateEntirelyInTheRequestedLanguage() {
        // When
        var mail = renderer.render(welcome(SPANISH, ENGLISH));

        // Then
        assertThat(mail.locale()).isEqualTo(SPANISH);
        assertThat(mail.subject()).isEqualTo("Tu código de Frappé");
        assertThat(mail.html())
                .contains(
                        "<html lang=\"es\"",
                        "<title>Tu código de Frappé</title>",
                        "¡Hola, Ana!",
                        "Tu código es 123456.")
                .doesNotContain("Hello", "Your code");
        assertThat(fallbacks()).isZero();
    }

    @Test
    void rendersTheWholeMailInTheFallbackLanguageWhenAKeyIsMissing() {
        // When
        var mail = renderer.render(welcome(PORTUGUESE, SPANISH));

        // Then nothing Portuguese remains, not even the lines that exist in Portuguese
        assertThat(mail.locale()).isEqualTo(SPANISH);
        assertThat(mail.subject()).isEqualTo("Tu código de Frappé");
        assertThat(mail.html())
                .contains("<html lang=\"es\"", "¡Hola, Ana!", "Tu código es 123456.")
                .doesNotContain("Olá", "Seu código");
        assertThat(registry.get("mail.locale.fallback")
                        .tag("mail.template", "mailprobe/welcome")
                        .tag("locale.requested", "pt")
                        .tag("locale.used", "es")
                        .counter()
                        .count())
                .isEqualTo(1);
    }

    @Test
    void rendersAnUnsupportedLanguageInTheFallbackLanguage() {
        // When
        var mail = renderer.render(welcome(Locale.FRENCH, SPANISH));

        // Then the tag stays bounded: any unsupported language counts as "unsupported"
        assertThat(mail.locale()).isEqualTo(SPANISH);
        assertThat(registry.get("mail.locale.fallback")
                        .tag("locale.requested", "unsupported")
                        .tag("locale.used", "es")
                        .counter()
                        .count())
                .isEqualTo(1);
    }

    @Test
    void rendersARegionalLocaleInItsLanguage() {
        // When
        var mail = renderer.render(welcome(Locale.forLanguageTag("es-AR"), ENGLISH));

        // Then
        assertThat(mail.locale()).isEqualTo(SPANISH);
        assertThat(fallbacks()).isZero();
    }

    @Test
    void endsInEnglishWhenTheFallbackLanguageIsIncompleteToo() {
        // When
        var mail = renderer.render(welcome(PORTUGUESE, PORTUGUESE));

        // Then
        assertThat(mail.locale()).isEqualTo(ENGLISH);
        assertThat(mail.html()).contains("Hello, Ana!", "Your code is 123456.");
    }

    @Test
    void failsNamingTheKeyWhenNoLanguageHasIt() {
        // Given
        var empty = new MailRenderer(
                TemplateEngine.createPrecompiled(ContentType.Html), new StaticMessageSource(), registry);

        // When / Then: a key in no catalog is a programming error
        assertThatIllegalStateException()
                .isThrownBy(() -> empty.render(welcome(SPANISH, ENGLISH)))
                .withMessageContaining("mailprobe.mail.welcome.subject")
                .withMessageContaining("mailprobe/welcome");
    }

    @Test
    void escapesModelValues() {
        // When
        var mail = renderer.render(MailMessage.of(
                "mailprobe/welcome",
                "ana@example.com",
                ENGLISH,
                ENGLISH,
                Map.of("name", "<script>alert(1)</script>", "code", "123456")));

        // Then
        assertThat(mail.html()).contains("&lt;script&gt;").doesNotContain("<script>");
    }

    @Test
    void rejectsAModelThatShadowsTheTexts() {
        // Given
        var message = MailMessage.of(
                "mailprobe/welcome", "ana@example.com", ENGLISH, ENGLISH, Map.of("texts", "x", "code", "1"));

        // When / Then
        assertThatIllegalArgumentException().isThrownBy(() -> renderer.render(message));
    }

    private static MailMessage welcome(Locale locale, Locale fallback) {
        return MailMessage.of(
                "mailprobe/welcome", "ana@example.com", locale, fallback, Map.of("name", "Ana", "code", "123456"));
    }

    private double fallbacks() {
        return registry.find("mail.locale.fallback").counters().stream()
                .mapToDouble(counter -> counter.count())
                .sum();
    }
}
