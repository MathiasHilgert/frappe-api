package com.frappe.platform.infrastructure.usecase;

import com.frappe.platform.Result;
import org.aopalliance.intercept.MethodInterceptor;
import org.aopalliance.intercept.MethodInvocation;
import org.jspecify.annotations.Nullable;
import org.springframework.transaction.interceptor.TransactionAspectSupport;

/**
 * Rolls back the transaction a use case started when it returns a {@link Result.Failure}: a business refusal leaves no
 * state and no outbox row behind, even when the use case saved or recorded events before refusing.
 *
 * <p>Ordered inside the transaction interceptor, it evaluates the return value before the interceptor commits, the way
 * Spring's transaction interceptor itself treats a failed Vavr {@code Try} or completed {@code Future} (Spring 7 has no
 * extension point for other return types). Unlike that built-in rule it only marks a transaction the use case started:
 * a joined transaction belongs to its owner, who decides from the returned failure.
 */
final class RollbackOnFailureInterceptor implements MethodInterceptor {

    /** Creates the interceptor; stateless. */
    RollbackOnFailureInterceptor() {}

    @Override
    public @Nullable Object invoke(MethodInvocation invocation) throws Throwable {
        var result = invocation.proceed();
        if (result instanceof Result.Failure<?, ?>) {
            // Throws NoTransactionException for a use case without @Transactional, which the architecture tests
            // reject: a broken invariant should fail loudly, not be skipped.
            var transaction = TransactionAspectSupport.currentTransactionStatus();
            if (transaction.isNewTransaction()) {
                transaction.setRollbackOnly();
            }
        }
        return result;
    }
}
