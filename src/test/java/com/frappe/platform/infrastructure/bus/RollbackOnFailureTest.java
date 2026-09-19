package com.frappe.platform.infrastructure.bus;

import static org.assertj.core.api.Assertions.assertThat;

import com.frappe.platform.Command;
import com.frappe.platform.CommandBus;
import com.frappe.platform.CommandHandler;
import com.frappe.platform.Result;
import io.micrometer.observation.ObservationRegistry;
import java.util.concurrent.atomic.AtomicInteger;
import org.junit.jupiter.api.Test;
import org.springframework.boot.test.context.runner.ApplicationContextRunner;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.transaction.TransactionDefinition;
import org.springframework.transaction.annotation.EnableTransactionManagement;
import org.springframework.transaction.annotation.Transactional;
import org.springframework.transaction.support.AbstractPlatformTransactionManager;
import org.springframework.transaction.support.DefaultTransactionStatus;
import org.springframework.transaction.support.TransactionTemplate;

/** Which transaction a returned failure rolls back, and which methods the rollback advice applies to. */
class RollbackOnFailureTest {

    private final ApplicationContextRunner contextRunner = new ApplicationContextRunner()
            .withUserConfiguration(BusConfiguration.class, TransactionConfiguration.class)
            .withBean(ObservationRegistry.class, ObservationRegistry::create)
            .withBean(RefundHandler.class);

    enum RefundError {
        ALREADY_REFUNDED
    }

    record Refund() implements Command<Result<String, RefundError>> {}

    static class RefundHandler implements CommandHandler<Refund, Result<String, RefundError>> {

        @Override
        @Transactional
        public Result<String, RefundError> handle(Refund command) {
            return Result.failure(RefundError.ALREADY_REFUNDED);
        }

        /** An unrelated one-argument overload, called without a transaction; the advice must not touch it. */
        public Result<String, RefundError> handle(String reason) {
            return Result.failure(RefundError.ALREADY_REFUNDED);
        }
    }

    /** Class-based transaction proxies, as Spring Boot configures them, so the handler is injectable by class. */
    @Configuration(proxyBeanMethods = false)
    @EnableTransactionManagement(proxyTargetClass = true)
    static class TransactionConfiguration {

        @Bean
        RecordingTransactionManager transactionManager() {
            return new RecordingTransactionManager();
        }
    }

    /** A resource-less transaction manager that supports joining and records how each transaction ends. */
    static class RecordingTransactionManager extends AbstractPlatformTransactionManager {

        private final ThreadLocal<Boolean> active = ThreadLocal.withInitial(() -> false);

        final AtomicInteger commits = new AtomicInteger();

        final AtomicInteger rollbacks = new AtomicInteger();

        final AtomicInteger participantRollbackMarks = new AtomicInteger();

        @Override
        protected Object doGetTransaction() {
            return new Object();
        }

        @Override
        protected boolean isExistingTransaction(Object transaction) {
            return active.get();
        }

        @Override
        protected void doBegin(Object transaction, TransactionDefinition definition) {
            active.set(true);
        }

        @Override
        protected void doCommit(DefaultTransactionStatus status) {
            commits.incrementAndGet();
        }

        @Override
        protected void doRollback(DefaultTransactionStatus status) {
            rollbacks.incrementAndGet();
        }

        @Override
        protected void doSetRollbackOnly(DefaultTransactionStatus status) {
            participantRollbackMarks.incrementAndGet();
        }

        @Override
        protected void doCleanupAfterCompletion(Object transaction) {
            active.set(false);
        }
    }

    @Test
    void aFailureInItsOwnTransactionRollsItBack() {
        contextRunner.run(context -> {
            // Given
            var transactions = context.getBean(RecordingTransactionManager.class);

            // When
            context.getBean(CommandBus.class).dispatch(new Refund());

            // Then
            assertThat(transactions.rollbacks).hasValue(1);
            assertThat(transactions.commits).hasValue(0);
        });
    }

    @Test
    void aFailureInAJoinedTransactionLeavesTheDecisionToItsOwner() {
        contextRunner.run(context -> {
            // Given
            var transactions = context.getBean(RecordingTransactionManager.class);
            var bus = context.getBean(CommandBus.class);

            // When
            new TransactionTemplate(transactions).executeWithoutResult(status -> bus.dispatch(new Refund()));

            // Then
            assertThat(transactions.participantRollbackMarks).hasValue(0);
            assertThat(transactions.rollbacks).hasValue(0);
            assertThat(transactions.commits).hasValue(1);
        });
    }

    @Test
    void anOverloadOfHandleIsNotAdvised() {
        contextRunner.run(context -> {
            // Given
            var handler = context.getBean(RefundHandler.class);

            // When
            var result = handler.handle("duplicate");

            // Then
            // Advised, it would ask for the transaction status and fail: the overload runs without a transaction.
            assertThat(result).isEqualTo(Result.failure(RefundError.ALREADY_REFUNDED));
        });
    }
}
