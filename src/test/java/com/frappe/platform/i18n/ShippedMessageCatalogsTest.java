package com.frappe.platform.i18n;

import static org.assertj.core.api.Assertions.assertThat;

import org.junit.jupiter.api.Test;

/** The catalog check of {@code ./gradlew check}: every catalog on the classpath is complete and valid ICU. */
class ShippedMessageCatalogsTest {

    @Test
    void everyModuleCatalogIsCompleteAndValid() {
        // Given
        var catalogs = MessageCatalogs.load(IcuMessageSource.CATALOG_LOCATIONS);

        // When
        var violations = MessageCatalogCheck.violations(catalogs);

        // Then: at least the test catalogs (i18n/sample) must be found, or a broken location pattern would silently
        // check nothing and pass
        assertThat(catalogs).isNotEmpty();
        assertThat(violations).extracting(CatalogViolation::describe).isEmpty();
    }
}
