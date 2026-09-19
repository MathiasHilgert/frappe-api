package com.frappe.platform.infrastructure.mail;

import com.frappe.platform.i18n.SupportedLocales;
import com.frappe.platform.mail.MailMessage;
import gg.jte.Content;
import gg.jte.TemplateEngine;
import gg.jte.output.StringOutput;
import io.micrometer.core.instrument.Counter;
import io.micrometer.core.instrument.MeterRegistry;
import java.util.HashMap;
import java.util.LinkedHashMap;
import java.util.LinkedHashSet;
import java.util.Locale;
import java.util.Map;
import java.util.Optional;
import java.util.SequencedSet;
import org.springframework.context.MessageSource;

/**
 * Renders a {@link MailMessage} in exactly one language: the module's JTE template for the body, its catalog texts, and
 * the MJML layout around it. If the requested language is unsupported or lacks any key the template or its subject
 * needs, the whole mail is rendered again in the fallback language (then English), never mixed, and
 * {@code mail.locale.fallback} counts it.
 */
final class MailRenderer {

    /** The template parameter that carries the {@link com.frappe.platform.mail.MailTexts}. */
    static final String TEXTS_PARAMETER = "texts";

    private static final String LAYOUT = "mail/layout.jte";

    private static final String UNSUPPORTED = "unsupported";

    private final TemplateEngine templates;

    private final MessageSource messages;

    private final MeterRegistry registry;

    /**
     * Creates the renderer.
     *
     * @param templates the precompiled JTE templates
     * @param messages the application message source
     * @param registry where fallbacks are counted
     */
    MailRenderer(TemplateEngine templates, MessageSource messages, MeterRegistry registry) {
        this.templates = templates;
        this.messages = messages;
        this.registry = registry;
    }

    /**
     * Renders a message in its language, or entirely in a fallback language.
     *
     * @param message the message
     * @return the subject and HTML, with the language used
     * @throws IllegalArgumentException if the model uses the reserved parameter {@code texts}
     * @throws IllegalStateException if no language has every key the template needs (a programming error)
     */
    RenderedMail render(MailMessage message) {
        if (message.model().containsKey(TEXTS_PARAMETER)) {
            throw new IllegalArgumentException("The model of %s must not use the reserved parameter '%s'"
                    .formatted(message.templateId(), TEXTS_PARAMETER));
        }
        var requested = supportedLanguageOf(message.locale());
        var missingByLocale = new LinkedHashMap<Locale, SequencedSet<String>>();
        for (var locale : candidates(requested, message.fallbackLocale())) {
            var texts = new CatalogMailTexts(messages, locale);
            var mail = renderIn(message, locale, texts);
            if (texts.missingKeys().isEmpty()) {
                countFallback(message, requested, locale);
                return mail;
            }
            missingByLocale.put(locale, texts.missingKeys());
        }
        throw new IllegalStateException("Mail template %s lacks catalog keys in every language: %s"
                .formatted(message.templateId(), missingByLocale));
    }

    private RenderedMail renderIn(MailMessage message, Locale locale, CatalogMailTexts texts) {
        var subject = texts.get(subjectKey(message.templateId()));
        var params = new HashMap<String, Object>(message.model());
        params.put(TEXTS_PARAMETER, texts);
        var body = new StringOutput();
        templates.render(message.templateId() + ".jte", params, body);
        var html = new StringOutput();
        Content bodyContent = output -> output.writeContent(body.toString());
        templates.render(LAYOUT, Map.of("lang", locale.toLanguageTag(), "subject", subject, "body", bodyContent), html);
        return new RenderedMail(subject, html.toString(), locale);
    }

    // identity/email-proof -> identity.mail.email-proof.subject
    private static String subjectKey(String templateId) {
        var slash = templateId.indexOf('/');
        return templateId.substring(0, slash) + ".mail." + templateId.substring(slash + 1) + ".subject";
    }

    private static SequencedSet<Locale> candidates(Optional<Locale> requested, Locale fallback) {
        var candidates = new LinkedHashSet<Locale>();
        requested.ifPresent(candidates::add);
        supportedLanguageOf(fallback).ifPresent(candidates::add);
        candidates.add(SupportedLocales.FALLBACK);
        return candidates;
    }

    // Mail locales come from a user's preference or a tenant's language, both already supported ones; a regional
    // variant (es-AR) keeps its language, anything else falls back as a whole.
    private static Optional<Locale> supportedLanguageOf(Locale locale) {
        return SupportedLocales.all().stream()
                .filter(supported -> supported.getLanguage().equals(locale.getLanguage()))
                .findFirst();
    }

    private void countFallback(MailMessage message, Optional<Locale> requested, Locale used) {
        if (requested.filter(used::equals).isPresent()) {
            return;
        }
        Counter.builder("mail.locale.fallback")
                .description("Mails rendered entirely in a fallback language because the requested one was"
                        + " unsupported or incomplete")
                .tag("mail.template", message.templateId())
                .tag("locale.requested", requested.map(Locale::toLanguageTag).orElse(UNSUPPORTED))
                .tag("locale.used", used.toLanguageTag())
                .register(registry)
                .increment();
    }
}
