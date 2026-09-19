package com.frappe.platform.infrastructure.usecase;

import java.util.List;
import java.util.concurrent.CopyOnWriteArrayList;
import java.util.concurrent.atomic.AtomicInteger;
import org.springframework.transaction.TransactionDefinition;
import org.springframework.transaction.TransactionSystemException;
import org.springframework.transaction.support.AbstractPlatformTransactionManager;
import org.springframework.transaction.support.DefaultTransactionStatus;

/**
 * A resource-less transaction manager for tests: supports joining, records how each transaction began and ended, and
 * fails commits on request.
 */
final class RecordingTransactionManager extends AbstractPlatformTransactionManager {

    /** Thrown by commits once {@link #failCommits()} was called. */
    static final TransactionSystemException COMMIT_FAILURE = new TransactionSystemException("commit failed");

    private final ThreadLocal<Boolean> active = ThreadLocal.withInitial(() -> false);

    private volatile boolean failCommits;

    /** Read-only flag of every transaction begun, in order. */
    final List<Boolean> readOnlyFlags = new CopyOnWriteArrayList<>();

    /** Completed commits. */
    final AtomicInteger commits = new AtomicInteger();

    /** Completed rollbacks. */
    final AtomicInteger rollbacks = new AtomicInteger();

    /** Times a participant marked the joined transaction rollback-only. */
    final AtomicInteger participantRollbackMarks = new AtomicInteger();

    /** Makes every later commit fail with {@link #COMMIT_FAILURE}. */
    void failCommits() {
        failCommits = true;
    }

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
        readOnlyFlags.add(definition.isReadOnly());
    }

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

    @Override
    protected void doSetRollbackOnly(DefaultTransactionStatus status) {
        participantRollbackMarks.incrementAndGet();
    }

    @Override
    protected void doCleanupAfterCompletion(Object transaction) {
        active.set(false);
    }
}
