package com.frappe.platform.infrastructure.i18n;

import static com.frappe.platform.i18n.SupportedLocales.ENGLISH;
import static com.frappe.platform.i18n.SupportedLocales.PORTUGUESE;
import static com.frappe.platform.i18n.SupportedLocales.SPANISH;
import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatExceptionOfType;

import com.frappe.platform.i18n.SupportedLocales;
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
                .contains("i18n/orders/messages_en.properties: key 'tabs.closed' must start with 'orders.', or be a"
                        + " Spring problem detail key (problemDetail.title.<exception> or problemDetail.<exception>)"
                        + " for an exception in com.frappe.orders");
    }

    @Test
    void acceptsSpringProblemDetailKeysForTheModulesOwnExceptions() {
        // Given
        var catalogs = everyLanguage(
                "orders",
                Map.of(
                        "problemDetail.title.com.frappe.orders.TabAlreadyClosedException", "Tab already closed",
                        "problemDetail.com.frappe.orders.TabAlreadyClosedException", "Tab {0} is closed",
                        "problemDetail.com.frappe.orders.TabAlreadyClosedException.paid", "Tab {0} is paid"));

        // When / Then
        assertThat(MessageCatalogCheck.violations(catalogs)).isEmpty();
    }

    @Test
    void letsOnlyPlatformLocalizeProblemDetailsOfFrameworkExceptions() {
        // Given
        var key = "problemDetail.title.org.springframework.web.HttpRequestMethodNotSupportedException";

        // When / Then
        assertThat(MessageCatalogCheck.violations(everyLanguage("platform", Map.of(key, "Method not allowed"))))
                .isEmpty();
        assertThat(MessageCatalogCheck.violations(everyLanguage("orders", Map.of(key, "Method not allowed"))))
                .extracting(CatalogViolation::describe)
                .contains("i18n/orders/messages_en.properties: key '%s' must start with 'orders.', or be a Spring"
                                .formatted(key)
                        + " problem detail key (problemDetail.title.<exception> or problemDetail.<exception>) for an"
                        + " exception in com.frappe.orders");
    }

    @Test
    void rejectsProblemTypesBecauseTheyAreStableUrisNotText() {
        // Given
        var catalogs = everyLanguage(
                "orders",
                Map.of("problemDetail.type.com.frappe.orders.TabAlreadyClosedException", "https://frappe.app/x"));

        // When / Then
        assertThat(MessageCatalogCheck.violations(catalogs))
                .extracting(CatalogViolation::describe)
                .contains("i18n/orders/messages_en.properties: key"
                        + " 'problemDetail.type.com.frappe.orders.TabAlreadyClosedException' is a problem type; types"
                        + " are stable URIs set in code, never translated");
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
        return SupportedLocales.all().stream()
                .map(locale -> new MessageCatalog(module, locale, messages))
                .toList();
    }
}
