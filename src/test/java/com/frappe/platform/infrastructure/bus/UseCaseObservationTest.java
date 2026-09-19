package com.frappe.platform.infrastructure.bus;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

import com.frappe.platform.Command;
import com.frappe.platform.CommandBus;
import com.frappe.platform.CommandHandler;
import com.frappe.platform.Query;
import com.frappe.platform.QueryBus;
import com.frappe.platform.QueryHandler;
import com.frappe.platform.Result;
import io.micrometer.observation.ObservationRegistry;
import io.micrometer.observation.tck.TestObservationRegistry;
import io.micrometer.observation.tck.TestObservationRegistryAssert;
import java.util.concurrent.atomic.AtomicInteger;
import org.junit.jupiter.api.Test;
import org.springframework.boot.test.context.runner.ApplicationContextRunner;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.transaction.TransactionDefinition;
import org.springframework.transaction.TransactionSystemException;
import org.springframework.transaction.annotation.EnableTransactionManagement;
import org.springframework.transaction.annotation.Transactional;
import org.springframework.transaction.support.AbstractPlatformTransactionManager;
import org.springframework.transaction.support.DefaultTransactionStatus;

/** Every dispatch is one stopped {@code use_case} observation whose outcome tells business refusals from defects. */
class UseCaseObservationTest {

    private final TestObservationRegistry observations = TestObservationRegistry.create();

    private final ApplicationContextRunner contextRunner = new ApplicationContextRunner()
            .withUserConfiguration(BusConfiguration.class, TransactionConfiguration.class)
            .withBean(ObservationRegistry.class, () -> observations);

    enum OrderError {
        OUT_OF_STOCK
    }

    record PlaceOrder(boolean inStock) implements Command<Result<String, OrderError>> {}

    record CancelOrder() implements Command<Result<String, OrderError>> {}

    record FindOrder() implements Query<String> {}

    static class PlaceOrderHandler implements CommandHandler<PlaceOrder, Result<String, OrderError>> {

        @Override
        @Transactional
        public Result<String, OrderError> handle(PlaceOrder command) {
            return command.inStock() ? Result.success("order-1") : Result.failure(OrderError.OUT_OF_STOCK);
        }
    }

    static class CancelOrderHandler implements CommandHandler<CancelOrder, Result<String, OrderError>> {

        static final IllegalStateException DEFECT = new IllegalStateException("broken invariant");

        @Override
        @Transactional
        public Result<String, OrderError> handle(CancelOrder command) {
            throw DEFECT;
        }
    }

    static class FindOrderHandler implements QueryHandler<FindOrder, String> {

        @Override
        @Transactional(readOnly = true)
        public String handle(FindOrder query) {
            return "order-1";
        }
    }

    /** Real transaction proxies around the handlers, over a transaction manager that can be told to fail commits. */
    @Configuration(proxyBeanMethods = false)
    @EnableTransactionManagement
    static class TransactionConfiguration {

        @Bean
        CommitFailingTransactionManager transactionManager() {
            return new CommitFailingTransactionManager();
        }
    }

    /** A transaction manager without a resource; its commit fails once {@link #failCommits()} was called. */
    static class CommitFailingTransactionManager extends AbstractPlatformTransactionManager {

        static final TransactionSystemException COMMIT_FAILURE = new TransactionSystemException("commit failed");

        private volatile boolean failCommits;

        final AtomicInteger commits = new AtomicInteger();

        final AtomicInteger rollbacks = new AtomicInteger();

        void failCommits() {
            failCommits = true;
        }

        @Override
        protected Object doGetTransaction() {
            return new Object();
        }

        @Override
        protected void doBegin(Object transaction, TransactionDefinition definition) {}

        @Override
        protected void doCommit(DefaultTransactionStatus status) {
            if (failCommits) {
                throw COMMIT_FAILURE;
            }
            commits.incrementAndGet();
        }

        @Override
        protected void doRollback(DefaultTransactionStatus status) {
            rollbacks.incrementAndGet();
        }
    }

    @Test
    void aSucceedingCommandIsObservedAsOneUseCaseWithOutcomeSuccess() {
        contextRunner.withBean(PlaceOrderHandler.class).run(context -> {
            // When
            context.getBean(CommandBus.class).dispatch(new PlaceOrder(true));

            // Then
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
    void aFailureResultIsObservedWithOutcomeFailureAndNoError() {
        contextRunner.withBean(PlaceOrderHandler.class).run(context -> {
            // When
            var result = context.getBean(CommandBus.class).dispatch(new PlaceOrder(false));

            // Then
            assertThat(result).isEqualTo(Result.failure(OrderError.OUT_OF_STOCK));
            TestObservationRegistryAssert.assertThat(observations)
                    .hasSingleObservationThat()
                    .hasLowCardinalityKeyValue("outcome", "failure")
                    .doesNotHaveError()
                    .hasBeenStopped();
        });
    }

    @Test
    void aFailureResultRollsBackTheTransactionInsteadOfCommittingIt() {
        contextRunner.withBean(PlaceOrderHandler.class).run(context -> {
            // Given
            var transactions = context.getBean(CommitFailingTransactionManager.class);

            // When
            context.getBean(CommandBus.class).dispatch(new PlaceOrder(false));

            // Then
            assertThat(transactions.rollbacks).hasValue(1);
            assertThat(transactions.commits).hasValue(0);
        });
    }

    @Test
    void aThrowingHandlerIsObservedWithOutcomeErrorAndItsExceptionRethrown() {
        contextRunner.withBean(CancelOrderHandler.class).run(context -> {
            // Given
            var bus = context.getBean(CommandBus.class);

            // When / Then
            assertThatThrownBy(() -> bus.dispatch(new CancelOrder())).isSameAs(CancelOrderHandler.DEFECT);
            TestObservationRegistryAssert.assertThat(observations)
                    .hasSingleObservationThat()
                    .hasLowCardinalityKeyValue("use_case.name", "CancelOrder")
                    .hasLowCardinalityKeyValue("outcome", "error")
                    .hasError(CancelOrderHandler.DEFECT)
                    .hasBeenStopped();
        });
    }

    @Test
    void aFailingCommitIsObservedWithOutcomeErrorBecauseTheCommitRunsInsideTheObservation() {
        contextRunner.withBean(PlaceOrderHandler.class).run(context -> {
            // Given
            context.getBean(CommitFailingTransactionManager.class).failCommits();
            var bus = context.getBean(CommandBus.class);

            // When / Then
            assertThatThrownBy(() -> bus.dispatch(new PlaceOrder(true)))
                    .isSameAs(CommitFailingTransactionManager.COMMIT_FAILURE);
            TestObservationRegistryAssert.assertThat(observations)
                    .hasSingleObservationThat()
                    .hasLowCardinalityKeyValue("outcome", "error")
                    .hasError(CommitFailingTransactionManager.COMMIT_FAILURE)
                    .hasBeenStopped();
        });
    }

    @Test
    void aQueryIsObservedAsAUseCaseOfKindQuery() {
        contextRunner.withBean(FindOrderHandler.class).run(context -> {
            // When
            context.getBean(QueryBus.class).ask(new FindOrder());

            // Then
            TestObservationRegistryAssert.assertThat(observations)
                    .hasSingleObservationThat()
                    .hasNameEqualTo("use_case")
                    .hasContextualNameEqualTo("platform FindOrder")
                    .hasLowCardinalityKeyValue("use_case.name", "FindOrder")
                    .hasLowCardinalityKeyValue("use_case.kind", "query")
                    .hasLowCardinalityKeyValue("outcome", "success")
                    .hasBeenStopped();
        });
    }

    @Test
    void aMessageWithoutAHandlerIsObservedAsAnError() {
        contextRunner.run(context -> {
            // Given
            var bus = context.getBean(QueryBus.class);

            // When / Then
            assertThatThrownBy(() -> bus.ask(new FindOrder())).isInstanceOf(MissingHandlerException.class);
            TestObservationRegistryAssert.assertThat(observations)
                    .hasSingleObservationThat()
                    .hasLowCardinalityKeyValue("outcome", "error")
                    .hasError()
                    .hasBeenStopped();
        });
    }
}
