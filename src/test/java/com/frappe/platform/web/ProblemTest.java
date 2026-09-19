package com.frappe.platform.web;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatIllegalArgumentException;
import static org.assertj.core.api.Assertions.assertThatNoException;
import static org.assertj.core.api.Assertions.assertThatNullPointerException;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

import java.util.LinkedHashMap;
import java.util.Map;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.ValueSource;

/** A problem is the stable, technology-free contract a module maps its business failures to. */
class ProblemTest {

    @Test
    void carriesStatusSlugMessageKeyAndItsParamsInOrder() {
        // When
        var problem = Problem.of(409, "tab-already-closed", "order.tab.already-closed")
                .with("tabId", "T-12")
                .with("closedBy", "Ana");

        // Then
        assertThat(problem.status()).isEqualTo(409);
        assertThat(problem.slug()).isEqualTo("tab-already-closed");
        assertThat(problem.messageKey()).isEqualTo("order.tab.already-closed");
        assertThat(problem.params()).containsExactly(Map.entry("tabId", "T-12"), Map.entry("closedBy", "Ana"));
        assertThat(problem.arguments()).containsExactly("T-12", "Ana");
    }

    @Test
    void itsParamsCannotBeChangedFromOutside() {
        // Given
        var params = new LinkedHashMap<String, Object>();
        params.put("tabId", "T-12");
        var problem = new Problem(409, "tab-already-closed", "order.tab.already-closed", params);

        // When
        params.put("other", "value");

        // Then
        assertThat(problem.params()).containsOnlyKeys("tabId");
        assertThatThrownBy(() -> problem.params().put("x", "y")).isInstanceOf(UnsupportedOperationException.class);
    }

    @ParameterizedTest
    @ValueSource(ints = {200, 399, 500, 503})
    void aBusinessFailureIsAClientError(int status) {
        assertThatIllegalArgumentException()
                .isThrownBy(() -> Problem.of(status, "tab-already-closed", "order.tab.already-closed"))
                .withMessageContaining(String.valueOf(status))
                .withMessageContaining("4xx");
    }

    @ParameterizedTest
    @ValueSource(strings = {"", "Tab-Closed", "tab_closed", "tab--closed", "-tab", "tab closed"})
    void theSlugIsKebabCase(String slug) {
        assertThatIllegalArgumentException()
                .isThrownBy(() -> Problem.of(409, slug, "order.tab.already-closed"))
                .withMessageContaining("kebab-case");
    }

    @Test
    void theMessageKeyIsRequired() {
        assertThatIllegalArgumentException().isThrownBy(() -> Problem.of(409, "tab-already-closed", " "));
    }

    enum Channel {
        DINE_IN
    }

    @Test
    void paramsAreRawClientValues() {
        // When / Then
        assertThatNoException()
                .isThrownBy(() -> Problem.of(409, "tab-already-closed", "order.tab.already-closed")
                        .with("text", "T-12")
                        .with("count", 3)
                        .with("amount", new java.math.BigDecimal("12.50"))
                        .with("open", false)
                        .with("id", java.util.UUID.randomUUID())
                        .with("channel", Channel.DINE_IN)
                        .with("closedAt", java.time.Instant.EPOCH)
                        .with("day", java.time.LocalDate.EPOCH));
        assertThatIllegalArgumentException()
                .isThrownBy(() -> Problem.of(409, "tab-already-closed", "order.tab.already-closed")
                        .with("tab", new Object()))
                .withMessageContaining("tab")
                .withMessageContaining("String, Number, Boolean, UUID, enum or java.time");
        assertThatIllegalArgumentException()
                .isThrownBy(() -> Problem.of(409, "tab-already-closed", "order.tab.already-closed")
                        .with("items", java.util.List.of("a")));
    }

    @Test
    void paramsHaveNamesAndValues() {
        var problem = Problem.of(409, "tab-already-closed", "order.tab.already-closed");

        assertThatNullPointerException().isThrownBy(() -> problem.with("tabId", null));
        assertThatIllegalArgumentException().isThrownBy(() -> problem.with(" ", "T-12"));
    }
}
