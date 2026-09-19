package com.frappe.platform.infrastructure.usecase;

import com.frappe.platform.CommandUseCase;
import com.frappe.platform.QueryUseCase;
import com.frappe.platform.Result;
import java.lang.reflect.Method;
import java.lang.reflect.Modifier;
import java.util.Optional;
import org.springframework.aop.support.StaticMethodMatcherPointcut;
import org.springframework.core.annotation.AnnotationUtils;
import org.springframework.util.ReflectionUtils;

/**
 * Matches the operation of a use case: the public methods of a class marked {@link CommandUseCase} or
 * {@link QueryUseCase}, never the methods it inherits from {@code Object}.
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
        if (AnnotationUtils.findAnnotation(type, CommandUseCase.class) != null) {
            return Optional.of(UseCaseKind.COMMAND);
        }
        if (AnnotationUtils.findAnnotation(type, QueryUseCase.class) != null) {
            return Optional.of(UseCaseKind.QUERY);
        }
        return Optional.empty();
    }

    @Override
    public boolean matches(Method method, Class<?> targetClass) {
        return kindOf(targetClass).isPresent()
                && Modifier.isPublic(method.getModifiers())
                && !ReflectionUtils.isObjectMethod(method)
                && (!resultReturningOnly || Result.class.isAssignableFrom(method.getReturnType()));
    }
}
