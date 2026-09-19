package com.frappe.platform.infrastructure.bus;

import com.frappe.platform.Command;
import com.frappe.platform.CommandHandler;
import com.frappe.platform.Result;
import java.lang.reflect.Method;
import org.aopalliance.intercept.MethodInterceptor;
import org.aopalliance.intercept.MethodInvocation;
import org.jspecify.annotations.Nullable;
import org.springframework.aop.framework.AbstractAdvisingBeanPostProcessor;
import org.springframework.aop.support.AopUtils;
import org.springframework.aop.support.StaticMethodMatcherPointcutAdvisor;
import org.springframework.transaction.interceptor.TransactionAspectSupport;
import org.springframework.util.ClassUtils;

/**
 * Rolls back the transaction a command handler started when it returns a {@link Result.Failure}: an expected business refusal
 * must leave no state and no outbox row behind, even when the handler saved or recorded events before refusing.
 *
 * <p>Ordering: Spring's auto-proxy creator (highest precedence) has already wrapped the handler in its transaction
 * proxy when this post-processor (lowest precedence) runs, and {@link AbstractAdvisingBeanPostProcessor} appends its
 * advisor to the end of that proxy's chain. The advice therefore runs inside the transaction interceptor and marks the
 * transaction rollback-only before the interceptor would commit; the failure is still returned, not thrown. Beans
 * without a transaction proxy are left alone (startup rejects such command handlers anyway).
 */
final class RollbackOnFailurePostProcessor extends AbstractAdvisingBeanPostProcessor {

    private static final long serialVersionUID = 1L;

    /** Creates the post-processor with its advisor on {@code CommandHandler#handle}. */
    RollbackOnFailurePostProcessor() {
        this.advisor = new RollbackOnFailureAdvisor();
    }

    // Only ever extend an existing transaction proxy, never create a proxy of our own: so neither the bean nor its
    // predicted type changes here (a predicted JDK proxy type would break injection of the handler by its class).
    @Override
    protected boolean isEligible(Object bean, String beanName) {
        return false;
    }

    @Override
    public Class<?> determineBeanType(Class<?> beanClass, String beanName) {
        return beanClass;
    }

    /** Matches {@code handle} of every command handler and marks its transaction rollback-only on a failure. */
    private static final class RollbackOnFailureAdvisor extends StaticMethodMatcherPointcutAdvisor {

        private static final long serialVersionUID = 1L;

        private static final Method HANDLE = ClassUtils.getMethod(CommandHandler.class, "handle", Command.class);

        private RollbackOnFailureAdvisor() {
            super(new RollbackOnFailureInterceptor());
        }

        @Override
        public boolean matches(Method method, Class<?> targetClass) {
            if (!CommandHandler.class.isAssignableFrom(targetClass)) {
                return false;
            }
            // Both sides resolved to the implementation (bridge methods included): an unrelated one-argument
            // overload of handle is not the command handler's handle.
            var implementation = AopUtils.getMostSpecificMethod(HANDLE, targetClass);
            return AopUtils.getMostSpecificMethod(method, targetClass).equals(implementation);
        }
    }

    /**
     * Marks the handler's transaction rollback-only when it returned a failure, but only a transaction the handler
     * started: a joined transaction belongs to its owner, who decides from the returned failure.
     */
    private static final class RollbackOnFailureInterceptor implements MethodInterceptor {

        @Override
        public @Nullable Object invoke(MethodInvocation invocation) throws Throwable {
            var result = invocation.proceed();
            if (result instanceof Result.Failure<?, ?>) {
                var transaction = TransactionAspectSupport.currentTransactionStatus();
                if (transaction.isNewTransaction()) {
                    transaction.setRollbackOnly();
                }
            }
            return result;
        }
    }
}
