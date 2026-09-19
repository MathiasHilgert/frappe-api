package com.frappe.platform.i18n;

import static com.frappe.platform.i18n.SupportedLocales.ENGLISH;
import static com.frappe.platform.i18n.SupportedLocales.PORTUGUESE;
import static com.frappe.platform.i18n.SupportedLocales.SPANISH;
import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatExceptionOfType;

import java.util.List;
import java.util.Locale;
import java.util.Map;
import org.junit.jupiter.api.Test;

class MessageCatalogCheckTest {

    @Test
    void acceptsCompleteCatalogsWithValidIcuMessages() {
        // Given
        var catalogs = everyLanguage("orders", Map.of("orders.count", "{0, plural, one {# order} other {# orders}}"));

        // When / Then
        assertThat(MessageCatalogCheck.violations(catalogs)).isEmpty();
    }

    @Test
    void reportsAKeyMissingFromOneCatalogWithModuleLocaleAndKey() {
        // Given
        var catalogs = List.of(
                new MessageCatalog("orders", ENGLISH, Map.of("orders.placed", "Order placed")),
                new MessageCatalog("orders", SPANISH, Map.of()),
                new MessageCatalog("orders", PORTUGUESE, Map.of("orders.placed", "Pedido feito")));

        // When
        var violations = MessageCatalogCheck.violations(catalogs);

        // Then
        assertThat(violations)
                .extracting(CatalogViolation::describe)
                .containsExactly("i18n/orders/messages_es.properties: key 'orders.placed' is missing (present in en,"
                        + " pt); every catalog of a module has the same keys");
    }

    @Test
    void reportsEveryKeyOfAMissingCatalog() {
        // Given
        var catalogs = List.of(
                new MessageCatalog("orders", ENGLISH, Map.of("orders.placed", "Order placed")),
                new MessageCatalog("orders", SPANISH, Map.of("orders.placed", "Pedido realizado")));

        // When / Then
        assertThat(MessageCatalogCheck.violations(catalogs))
                .extracting(CatalogViolation::describe)
                .containsExactly("i18n/orders/messages_pt.properties: key 'orders.placed' is missing (present in en,"
                        + " es); every catalog of a module has the same keys");
    }

    @Test
    void reportsInvalidIcuSyntaxWithModuleLocaleAndKey() {
        // Given
        var catalogs = everyLanguage("orders", Map.of("orders.count", "{0, plural, one {# order}"));

        // When
        var violations = MessageCatalogCheck.violations(catalogs);

        // Then
        assertThat(violations).hasSize(3);
        assertThat(violations.getFirst().describe())
                .startsWith("i18n/orders/messages_en.properties: key 'orders.count' is not valid ICU MessageFormat"
                        + " syntax: ")
                .contains("plural");
    }

    @Test
    void reportsNamedArgumentsBecauseMessageSourcePassesNumberedOnes() {
        // Given
        var catalogs = everyLanguage("orders", Map.of("orders.greeting", "Hello, {name}!"));

        // When / Then
        assertThat(MessageCatalogCheck.violations(catalogs))
                .extracting(CatalogViolation::describe)
                .contains("i18n/orders/messages_pt.properties: key 'orders.greeting' uses named arguments; use"
                        + " numbered ones ({0}), which MessageSource passes");
    }

    @Test
    void reportsKeysOutsideTheModulesNamespace() {
        // Given
        var catalogs = everyLanguage("orders", Map.of("tabs.closed", "Tab closed"));

        // When / Then
        assertThat(MessageCatalogCheck.violations(catalogs))
                .extracting(CatalogViolation::describe)
                .contains("i18n/orders/messages_en.properties: key 'tabs.closed' must start with 'orders.', the"
                        + " module's namespace");
    }

    @Test
    void reportsACatalogInAnUnsupportedLocale() {
        // Given
        var catalogs = List.of(new MessageCatalog("orders", Locale.forLanguageTag("pt-BR"), Map.of()));

        // When / Then
        assertThat(MessageCatalogCheck.violations(catalogs))
                .extracting(CatalogViolation::describe)
                .containsExactly("i18n/orders/messages_pt_BR.properties: locale 'pt-BR' is not supported (en, es, pt)");
    }

    @Test
    void loadingInvalidCatalogsFailsListingEveryViolation() {
        // When / Then
        assertThatExceptionOfType(InvalidMessageCatalogsException.class)
                .isThrownBy(() -> IcuMessageSource.load("classpath*:i18n-fixtures/broken/*/messages_*.properties"))
                .withMessageContaining("i18n/orders/messages_es.properties: key 'orders.placed' is missing")
                .withMessageContaining(
                        "i18n/orders/messages_es.properties: key 'orders.count' is not valid ICU MessageFormat syntax");
    }

    private static List<MessageCatalog> everyLanguage(String module, Map<String, String> messages) {
        return SupportedLocales.all().locales().stream()
                .map(locale -> new MessageCatalog(module, locale, messages))
                .toList();
    }
}
