package com.frappe.platform.infrastructure.persistence;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;
import static org.mockito.Mockito.mock;
import static org.mockito.Mockito.when;

import java.sql.SQLException;
import java.util.List;
import java.util.UUID;
import java.util.concurrent.CopyOnWriteArrayList;
import javax.sql.DataSource;
import org.junit.jupiter.api.Test;
import org.springframework.jdbc.CannotGetJdbcConnectionException;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.transaction.TransactionDefinition;
import org.springframework.transaction.support.AbstractPlatformTransactionManager;
import org.springframework.transaction.support.DefaultTransactionDefinition;
import org.springframework.transaction.support.DefaultTransactionStatus;
import org.springframework.transaction.support.TransactionSynchronizationManager;

/**
 * A failing {@code set_config} must not leave the begun transaction behind: Spring's transaction manager does not roll
 * back when a listener's {@code afterBegin} throws, and the caller never receives the status to do it.
 */
class TenantTransactionListenerTest {

    static final UUID TENANT = UUID.fromString("0199a000-0000-7000-8000-00000000000a");

    @Test
    void aFailingTenantSettingRollsBackTheBegunTransactionAndRethrows() throws SQLException {
        // Given
        var dataSource = mock(DataSource.class);
        when(dataSource.getConnection()).thenThrow(new SQLException("database gone"));
        var scope = new ThreadBoundTenantScope();
        var transactions = new RecordingTransactionManager();
        transactions.addListener(
                new TenantTransactionListener(scope, new JdbcTemplate(dataSource), () -> transactions));

        // When
        assertThatThrownBy(() ->
                        scope.runAs(TENANT, () -> transactions.getTransaction(new DefaultTransactionDefinition())))
                .isInstanceOf(CannotGetJdbcConnectionException.class);

        // Then
        assertThat(transactions.events).containsExactly("begin", "rollback", "cleanup");
        assertThat(TransactionSynchronizationManager.isSynchronizationActive()).isFalse();
        assertThat(TransactionSynchronizationManager.isActualTransactionActive())
                .isFalse();
    }

    /** Records begin, rollback and cleanup of transactions that touch no resource. */
    static final class RecordingTransactionManager extends AbstractPlatformTransactionManager {

        final List<String> events = new CopyOnWriteArrayList<>();

        @Override
        protected Object doGetTransaction() {
            return new Object();
        }

        @Override
        protected void doBegin(Object transaction, TransactionDefinition definition) {
            events.add("begin");
        }

        @Override
        protected void doCommit(DefaultTransactionStatus status) {
            events.add("commit");
        }

        @Override
        protected void doRollback(DefaultTransactionStatus status) {
            events.add("rollback");
        }

        @Override
        protected void doCleanupAfterCompletion(Object transaction) {
            events.add("cleanup");
        }
    }
}
