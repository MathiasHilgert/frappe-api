package com.frappe.platform.infrastructure.usecase;

import io.micrometer.observation.Observation;
import io.micrometer.observation.ObservationRegistry;
import java.util.function.Supplier;
import org.aopalliance.intercept.MethodInterceptor;
import org.aopalliance.intercept.MethodInvocation;
import org.jspecify.annotations.Nullable;
import org.springframework.aop.support.AopUtils;

/**
 * Observes one use case call as the {@code use_case} observation (span {@code <module> <UseCase>}, timer
 * {@code use_case}). Ordered outside the transaction interceptor, so the commit is part of the observed call and a
 * failing commit is an {@code error}.
 */
final class UseCaseObservationInterceptor implements MethodInterceptor {

    private static final UseCaseObservationConvention CONVENTION = new UseCaseObservationConvention();

    // One description per use case class, computed on first call; ClassValue is the JDK's per-class cache. An
    // instance field: no state shared beyond the application context that owns the interceptor.
    private final ClassValue<UseCase> useCases = new ClassValue<>() {
        @Override
        protected UseCase computeValue(Class<?> type) {
            return UseCase.of(UseCasePointcut.kindOf(type).orElseThrow(), type);
        }
    };

    private final Supplier<ObservationRegistry> registry;

    /**
     * Creates the interceptor.
     *
     * @param registry where calls are observed, looked up on first use (advisors are created before most beans)
     */
    UseCaseObservationInterceptor(Supplier<ObservationRegistry> registry) {
        this.registry = registry;
    }

    @Override
    public @Nullable Object invoke(MethodInvocation invocation) throws Throwable {
        var target = invocation.getThis();
        var useCaseClass =
                target == null ? invocation.getMethod().getDeclaringClass() : AopUtils.getTargetClass(target);
        var context = new UseCaseObservationContext(useCases.get(useCaseClass));
        // observeChecked records any throwable as the observation's error, rethrows it and stops in finally; the
        // convention reads the outcome from the context when the observation stops.
        return Observation.createNotStarted(null, CONVENTION, () -> context, registry.get())
                .observeChecked(() -> {
                    var result = invocation.proceed();
                    context.returned(result);
                    return result;
                });
    }
}
