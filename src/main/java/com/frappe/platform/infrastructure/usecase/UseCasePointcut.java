package com.frappe.platform.infrastructure.usecase;

import com.frappe.platform.CommandUseCase;
import com.frappe.platform.QueryUseCase;
import com.frappe.platform.Result;
import java.lang.reflect.Method;
import java.lang.reflect.Modifier;
import java.util.Optional;
import org.springframework.aop.support.StaticMethodMatcherPointcut;
import org.springframework.core.BridgeMethodResolver;
import org.springframework.core.annotation.MergedAnnotations;
import org.springframework.core.annotation.MergedAnnotations.SearchStrategy;
import org.springframework.util.ReflectionUtils;

/**
 * Matches the operation of a use case: the public methods (inherited ones included) of a class marked
 * {@link CommandUseCase} or {@link QueryUseCase}, directly or through a composed annotation, never the methods of
 * {@code Object} nor synthetic ones. The marker is not inherited by subclasses. This is exactly what the use case scan
 * registers and what the architecture tests check.
 */
class UseCasePointcut extends StaticMethodMatcherPointcut {

    private final boolean resultReturningOnly;

    private UseCasePointcut(boolean resultReturningOnly) {
        this.resultReturningOnly = resultReturningOnly;
    }

    /**
     * Matches every operation of a use case.
     *
     * @return the pointcut
     */
    static UseCasePointcut operations() {
        return new UseCasePointcut(false);
    }

    /**
     * Matches the operations of a use case that return a {@link Result}.
     *
     * @return the pointcut
     */
    static UseCasePointcut resultReturningOperations() {
        return new UseCasePointcut(true);
    }

    /**
     * The kind of a use case class.
     *
     * @param type the class, not its proxy
     * @return the kind, or empty when the class is no use case
     */
    static Optional<UseCaseKind> kindOf(Class<?> type) {
        var annotations = MergedAnnotations.from(type, SearchStrategy.DIRECT);
        if (annotations.isPresent(CommandUseCase.class)) {
            return Optional.of(UseCaseKind.COMMAND);
        }
        if (annotations.isPresent(QueryUseCase.class)) {
            return Optional.of(UseCaseKind.QUERY);
        }
        return Optional.empty();
    }

    @Override
    public boolean matches(Method method, Class<?> targetClass) {
        // A call through a generic interface arrives as the compiler's bridge method; judge the method it bridges to.
        var operation = BridgeMethodResolver.findBridgedMethod(method);
        return kindOf(targetClass).isPresent()
                && Modifier.isPublic(operation.getModifiers())
                && !operation.isSynthetic()
                && !ReflectionUtils.isObjectMethod(operation)
                && (!resultReturningOnly || Result.class.isAssignableFrom(operation.getReturnType()));
    }
}
