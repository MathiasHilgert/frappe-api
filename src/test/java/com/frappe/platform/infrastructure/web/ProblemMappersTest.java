package com.frappe.platform.infrastructure.web;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatIllegalStateException;

import com.frappe.platform.web.Problem;
import com.frappe.platform.web.ProblemMapper;
import java.util.List;
import org.junit.jupiter.api.Test;

/** One mapper per failure type: the registry finds it, and refuses to start with two that could answer one failure. */
class ProblemMappersTest {

    enum TabError {
        ALREADY_CLOSED {
            @Override
            public String toString() {
                return "closed"; // a constant with a body is an anonymous subclass of the enum
            }
        },
        NOT_FOUND
    }

    sealed interface OrderError permits OrderError.Missing, OrderError.TooLarge {
        record Missing(String orderId) implements OrderError {}

        record TooLarge(int items, int limit) implements OrderError {}
    }

    enum Unmapped {
        ANYTHING
    }

    static final ProblemMapper<TabError> TABS = mapper(TabError.class, error -> switch (error) {
        case ALREADY_CLOSED -> Problem.of(409, "tab-already-closed", "order.tab.already-closed");
        case NOT_FOUND -> Problem.of(404, "tab-not-found", "order.tab.not-found");
    });

    static final ProblemMapper<OrderError> ORDERS = mapper(OrderError.class, error -> switch (error) {
        case OrderError.Missing missing ->
            Problem.of(404, "order-not-found", "order.order.not-found").with("orderId", missing.orderId());
        case OrderError.TooLarge tooLarge ->
            Problem.of(422, "order-too-large", "order.order.too-large").with("limit", tooLarge.limit());
    });

    @Test
    void findsTheMapperOfAFailureByItsType() {
        // Given
        var mappers = new ProblemMappers(List.of(TABS, ORDERS));

        // When / Then
        assertThat(mappers.problemOf(TabError.ALREADY_CLOSED))
                .contains(Problem.of(409, "tab-already-closed", "order.tab.already-closed"));
        assertThat(mappers.problemOf(new OrderError.TooLarge(12, 10)))
                .contains(Problem.of(422, "order-too-large", "order.order.too-large")
                        .with("limit", 10));
    }

    @Test
    void aFailureWithoutAMapperHasNoProblem() {
        assertThat(new ProblemMappers(List.of(TABS)).problemOf(Unmapped.ANYTHING))
                .isEmpty();
    }

    @Test
    void twoMappersForOneFailureTypeFailStartupNamingTheType() {
        assertThatIllegalStateException()
                .isThrownBy(() -> new ProblemMappers(List.of(TABS, ORDERS, mapper(TabError.class, error -> null))))
                .withMessageContaining(TabError.class.getName())
                .withMessageContaining("one ProblemMapper per failure type");
    }

    @Test
    void aMapperForASubtypeOfAnotherMappersTypeFailsStartup() {
        var missingOnly = mapper(OrderError.Missing.class, error -> Problem.of(404, "x", "order.x"));

        assertThatIllegalStateException()
                .isThrownBy(() -> new ProblemMappers(List.of(ORDERS, missingOnly)))
                .withMessageContaining(OrderError.class.getName())
                .withMessageContaining(OrderError.Missing.class.getName());
    }

    private static <E> ProblemMapper<E> mapper(Class<E> type, java.util.function.Function<E, Problem> problems) {
        return new ProblemMapper<>() {
            @Override
            public Class<E> failureType() {
                return type;
            }

            @Override
            public Problem problemOf(E failure) {
                return problems.apply(failure);
            }
        };
    }
}
