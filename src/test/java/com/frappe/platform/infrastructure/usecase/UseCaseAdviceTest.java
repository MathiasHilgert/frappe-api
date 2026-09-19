package com.frappe.platform.infrastructure.usecase;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

import com.frappe.platform.CommandUseCase;
import com.frappe.platform.QueryUseCase;
import com.frappe.platform.Result;
import io.micrometer.observation.ObservationRegistry;
import io.micrometer.observation.tck.TestObservationRegistry;
import io.micrometer.observation.tck.TestObservationRegistryAssert;
import java.lang.annotation.ElementType;
import java.lang.annotation.Retention;
import java.lang.annotation.RetentionPolicy;
import java.lang.annotation.Target;
import org.junit.jupiter.api.Test;
import org.springframework.boot.test.context.runner.ApplicationContextRunner;
import org.springframework.transaction.annotation.Transactional;
import org.springframework.transaction.support.TransactionTemplate;

/**
 * A use case is a plain Spring component called directly; the platform wraps it in one observation, the transaction
 * of its stereotype, and a rollback when it returns a failure.
 */
class UseCaseAdviceTest {

    private final TestObservationRegistry observations = TestObservationRegistry.create();

    private final ApplicationContextRunner contextRunner = new ApplicationContextRunner()
            .withUserConfiguration(UseCaseConfiguration.class)
            .withBean(RecordingTransactionManager.class)
            .withBean(ObservationRegistry.class, () -> observations)
            .withBean(PlaceOrder.class)
            .withBean(FindOrder.class)
            .withBean(CancelOrder.class)
            .withBean(ExtendedFindArchivedOrder.class);

    enum OrderError {
        OUT_OF_STOCK
    }

    @CommandUseCase
    static class PlaceOrder {

        static final IllegalStateException DEFECT = new IllegalStateException("broken invariant");

        @Transactional
        public Result<String, OrderError> place(String item) {
            return switch (item) {
                case "defect" -> throw DEFECT;
                case "sold out" -> Result.failure(OrderError.OUT_OF_STOCK);
                default -> Result.success("order-1");
            };
        }
    }

    @QueryUseCase
    static class FindOrder {

        @Transactional(readOnly = true)
        public String find(String id) {
            return "order " + id;
        }
    }

    /** A composed stereotype: meta-annotated use cases are use cases, as for the scan and the architecture rules. */
    @CommandUseCase
    @Retention(RetentionPolicy.RUNTIME)
    @Target(ElementType.TYPE)
    @interface OrderCommand {}

    @OrderCommand
    static class CancelOrder {

        @Transactional
        public Result<String, OrderError> cancel(String id) {
            return Result.success(id);
        }
    }

    @QueryUseCase
    static class FindArchivedOrder {

        @Transactional(readOnly = true)
        public String find(String id) {
            return "archived order " + id;
        }
    }

    /** Not marked itself: the stereotype is not inherited, as for the scan and the architecture rules. */
    static class ExtendedFindArchivedOrder extends FindArchivedOrder {}

    @Test
    void aUseCaseMarkedThroughAComposedAnnotationIsObserved() {
        contextRunner.run(context -> {
            // When
            context.getBean(CancelOrder.class).cancel("7");

            // Then
            TestObservationRegistryAssert.assertThat(observations)
                    .hasSingleObservationThat()
                    .hasContextualNameEqualTo("platform CancelOrder")
                    .hasLowCardinalityKeyValue("use_case.kind", "command");
        });
    }

    @Test
    void aSubclassOfAUseCaseIsNoUseCaseOfItsOwn() {
        contextRunner.run(context -> {
            // When
            context.getBean(ExtendedFindArchivedOrder.class).find("7");

            // Then
            TestObservationRegistryAssert.assertThat(observations).doesNotHaveAnyObservation();
        });
    }

    @Test
    void aCommandRunsInOneCommittedTransactionInsideOneObservation() {
        contextRunner.run(context -> {
            // When
            var result = context.getBean(PlaceOrder.class).place("coffee");

            // Then
            assertThat(result).isEqualTo(Result.success("order-1"));
            var transactions = context.getBean(RecordingTransactionManager.class);
            assertThat(transactions.readOnlyFlags).containsExactly(false);
            assertThat(transactions.commits).hasValue(1);
            TestObservationRegistryAssert.assertThat(observations)
                    .hasSingleObservationThat()
                    .hasNameEqualTo("use_case")
                    .hasContextualNameEqualTo("platform PlaceOrder")
                    .hasLowCardinalityKeyValue("use_case.name", "PlaceOrder")
                    .hasLowCardinalityKeyValue("use_case.module", "platform")
                    .hasLowCardinalityKeyValue("use_case.kind", "command")
                    .hasLowCardinalityKeyValue("outcome", "success")
                    .doesNotHaveError()
                    .hasBeenStopped();
        });
    }

    @Test
    void aFailureRollsBackItsOwnTransactionAndIsObservedAsFailure() {
        contextRunner.run(context -> {
            // When
            var result = context.getBean(PlaceOrder.class).place("sold out");

            // Then
            assertThat(result).isEqualTo(Result.failure(OrderError.OUT_OF_STOCK));
            var transactions = context.getBean(RecordingTransactionManager.class);
            assertThat(transactions.rollbacks).hasValue(1);
            assertThat(transactions.commits).hasValue(0);
            TestObservationRegistryAssert.assertThat(observations)
                    .hasSingleObservationThat()
                    .hasLowCardinalityKeyValue("outcome", "failure")
                    .doesNotHaveError()
                    .hasBeenStopped();
        });
    }

    @Test
    void aFailureInAJoinedTransactionLeavesTheDecisionToItsOwner() {
        contextRunner.run(context -> {
            // Given
            var transactions = context.getBean(RecordingTransactionManager.class);
            var placeOrder = context.getBean(PlaceOrder.class);

            // When
            new TransactionTemplate(transactions).executeWithoutResult(status -> placeOrder.place("sold out"));

            // Then
            assertThat(transactions.participantRollbackMarks).hasValue(0);
            assertThat(transactions.rollbacks).hasValue(0);
            assertThat(transactions.commits).hasValue(1);
        });
    }

    @Test
    void aThrowingCommandIsObservedAsErrorWithItsExceptionRethrown() {
        contextRunner.run(context -> {
            // When / Then
            assertThatThrownBy(() -> context.getBean(PlaceOrder.class).place("defect"))
                    .isSameAs(PlaceOrder.DEFECT);
            assertThat(context.getBean(RecordingTransactionManager.class).rollbacks)
                    .hasValue(1);
            TestObservationRegistryAssert.assertThat(observations)
                    .hasSingleObservationThat()
                    .hasLowCardinalityKeyValue("outcome", "error")
                    .hasError(PlaceOrder.DEFECT)
                    .hasBeenStopped();
        });
    }

    @Test
    void aFailingCommitIsObservedAsErrorBecauseTheTransactionRunsInsideTheObservation() {
        contextRunner.run(context -> {
            // Given
            context.getBean(RecordingTransactionManager.class).failCommits();
            var placeOrder = context.getBean(PlaceOrder.class);

            // When / Then
            assertThatThrownBy(() -> placeOrder.place("coffee")).isSameAs(RecordingTransactionManager.COMMIT_FAILURE);
            TestObservationRegistryAssert.assertThat(observations)
                    .hasSingleObservationThat()
                    .hasLowCardinalityKeyValue("outcome", "error")
                    .hasError(RecordingTransactionManager.COMMIT_FAILURE)
                    .hasBeenStopped();
        });
    }

    @Test
    void aQueryRunsInAReadOnlyTransactionAndIsObservedAsKindQuery() {
        contextRunner.run(context -> {
            // When
            var answer = context.getBean(FindOrder.class).find("7");

            // Then
            assertThat(answer).isEqualTo("order 7");
            assertThat(context.getBean(RecordingTransactionManager.class).readOnlyFlags)
                    .containsExactly(true);
            TestObservationRegistryAssert.assertThat(observations)
                    .hasSingleObservationThat()
                    .hasContextualNameEqualTo("platform FindOrder")
                    .hasLowCardinalityKeyValue("use_case.kind", "query")
                    .hasLowCardinalityKeyValue("outcome", "success")
                    .hasBeenStopped();
        });
    }

    @Test
    void objectMethodsOfAUseCaseAreNeitherObservedNorTransactional() {
        contextRunner.run(context -> {
            // When
            context.getBean(PlaceOrder.class).toString();

            // Then
            TestObservationRegistryAssert.assertThat(observations).doesNotHaveAnyObservation();
            assertThat(context.getBean(RecordingTransactionManager.class).readOnlyFlags)
                    .isEmpty();
        });
    }
}
