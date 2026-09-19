package com.frappe.platform.infrastructure.i18n;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatExceptionOfType;

import com.frappe.platform.i18n.SupportedLocales;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.Test;
import org.springframework.context.i18n.LocaleContextHolder;

class MessageSourceMessagesTest {

    private final MessageSourceMessages messages =
            new MessageSourceMessages(IcuMessageSource.load("classpath*:i18n/sample/messages_*.properties"));

    @AfterEach
    void resetLocale() {
        LocaleContextHolder.resetLocaleContext();
    }

    @Test
    void resolvesInTheLocaleOfTheCurrentRequest() {
        // Given
        LocaleContextHolder.setLocale(SupportedLocales.PORTUGUESE);

        // When / Then
        assertThat(messages.get("sample.items", 2)).isEqualTo("2 itens");
        assertThat(messages.get("sample.greeting", "Ana")).isEqualTo("Olá, Ana!");
    }

    @Test
    void resolvesInAnExplicitLocaleOutsideARequest() {
        // When / Then
        assertThat(messages.get(SupportedLocales.SPANISH, "sample.items", 1)).isEqualTo("1 artículo");
    }

    @Test
    void resolvesAMessageWithoutArguments() {
        // Given
        LocaleContextHolder.setLocale(SupportedLocales.ENGLISH);

        // When / Then
        assertThat(messages.get("sample.quoted")).isEqualTo("Use {braces} for arguments");
    }

    @Test
    void anUnknownKeyFailsNamingKeyAndLocale() {
        // When / Then
        assertThatExceptionOfType(UnknownMessageKeyException.class)
                .isThrownBy(() -> messages.get(SupportedLocales.SPANISH, "sample.unknown"))
                .withMessageContaining("'sample.unknown'")
                .withMessageContaining("es")
                .withMessageContaining("i18n/sample/messages_{en,es,pt}.properties");
    }
}
