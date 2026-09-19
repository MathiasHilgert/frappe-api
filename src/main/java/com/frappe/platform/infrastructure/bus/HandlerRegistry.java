package com.frappe.platform.infrastructure.bus;

import java.lang.reflect.Method;
import java.lang.reflect.Modifier;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.stream.Collectors;
import org.jspecify.annotations.Nullable;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.beans.factory.ListableBeanFactory;
import org.springframework.core.ResolvableType;
import org.springframework.transaction.annotation.AnnotationTransactionAttributeSource;
import org.springframework.transaction.interceptor.TransactionAttributeSource;
import org.springframework.util.ClassUtils;

/**
 * The handler beans of one kind, keyed by the message type they handle. Built once at startup from the bean
 * definitions, without instantiating a handler (a handler may itself depend on a bus): every rule violation is
 * collected and reported together, so a routing gap stops the application instead of reaching a customer.
 */
final class HandlerRegistry {

    private static final Logger log = LoggerFactory.getLogger(HandlerRegistry.class);

    // The rule Spring's transaction proxy itself applies (public methods, method before class, bridge methods
    // resolved), so the startup check can never disagree with what happens at runtime.
    private static final TransactionAttributeSource TRANSACTION_ATTRIBUTES = new AnnotationTransactionAttributeSource();

    private final UseCaseKind kind;
    private final ListableBeanFactory beans;
    private final Map<Class<?>, String> beanNamesByMessageType;

    private HandlerRegistry(UseCaseKind kind, ListableBeanFactory beans, Map<Class<?>, String> beanNamesByMessageType) {
        this.kind = kind;
        this.beans = beans;
        this.beanNamesByMessageType = Map.copyOf(beanNamesByMessageType);
    }

    /**
     * Finds and validates every handler bean of a kind.
     *
     * @param beans the bean factory holding the handlers
     * @param kind command or query
     * @return the registry
     * @throws InvalidHandlersException if two handlers share a message type, a handler's message type cannot be
     *     resolved to a concrete class, the message lives outside a module package, or a command handler is not
     *     transactional
     */
    static HandlerRegistry discover(ListableBeanFactory beans, UseCaseKind kind) {
        var beanNamesByType = new LinkedHashMap<Class<?>, List<String>>();
        var problems = new ArrayList<String>();
        for (var beanName : beans.getBeanNamesForType(kind.handlerType())) {
            var handlerClass = handlerClassOf(beans, beanName);
            var messageType = messageTypeOf(kind, handlerClass);
            if (handlerClass == null || messageType == null) {
                problems.add(
                        "cannot tell which " + kind.tagValue() + " bean '" + beanName + "' handles; declare it as a"
                                + " class implementing " + kind.handlerType().getSimpleName() + "<Your"
                                + capitalized(kind.tagValue()) + ", YourResult> with a concrete " + kind.tagValue()
                                + " record");
                continue;
            }
            if (UseCase.moduleOf(messageType).isEmpty()) {
                problems.add(messageType.getName() + " (handled by '" + beanName + "') must live in a module package"
                        + " 'com.frappe.<module>'; the module of its use case is derived from it");
            }
            if (kind.transactional() && !isTransactional(kind, handlerClass)) {
                problems.add("'" + beanName + "' must be @Transactional (on handle or the class), so the state it"
                        + " saves and the events it records commit together");
            }
            beanNamesByType
                    .computeIfAbsent(messageType, type -> new ArrayList<>())
                    .add(beanName);
        }
        beanNamesByType.forEach((messageType, beanNames) -> {
            if (beanNames.size() > 1) {
                problems.add(messageType.getName() + " has " + beanNames.size() + " handlers ("
                        + beanNames.stream().map(name -> "'" + name + "'").collect(Collectors.joining(", "))
                        + "); exactly one handler per " + kind.tagValue() + " is allowed");
            }
        });
        if (!problems.isEmpty()) {
            throw new InvalidHandlersException(kind, problems);
        }
        log.atInfo()
                .addKeyValue(LogFields.USE_CASE_KIND, kind.tagValue())
                .addKeyValue(LogFields.HANDLER_COUNT, beanNamesByType.size())
                .log("Registered {} {} handlers", beanNamesByType.size(), kind.tagValue());
        var registered = new LinkedHashMap<Class<?>, String>();
        beanNamesByType.forEach((messageType, beanNames) -> registered.put(messageType, beanNames.getFirst()));
        return new HandlerRegistry(kind, beans, registered);
    }

    /**
     * The handler of a message type.
     *
     * @param messageType the exact type of the message
     * @return the handler bean (the transactional proxy for a command handler)
     * @throws MissingHandlerException if no handler is declared for the type
     */
    Object handlerFor(Class<?> messageType) {
        var beanName = beanNamesByMessageType.get(messageType);
        if (beanName == null) {
            throw new MissingHandlerException(kind, messageType);
        }
        return beans.getBean(beanName, kind.handlerType());
    }

    private static @Nullable Class<?> handlerClassOf(ListableBeanFactory beans, String beanName) {
        var beanType = beans.getType(beanName);
        // A transactional handler may already be its CGLIB subclass; the declared class carries the generics.
        return beanType == null ? null : ClassUtils.getUserClass(beanType);
    }

    private static @Nullable Class<?> messageTypeOf(UseCaseKind kind, @Nullable Class<?> handlerClass) {
        if (handlerClass == null) {
            return null;
        }
        // An unresolved type variable (lambda, generic handler class) resolves to its bound, the marker interface;
        // routing is by exact message class, so only a concrete class is a valid key.
        var messageType = ResolvableType.forClass(kind.handlerType(), handlerClass)
                .getGeneric(0)
                .resolve();
        if (messageType == null || messageType.isInterface() || Modifier.isAbstract(messageType.getModifiers())) {
            return null;
        }
        return messageType;
    }

    private static boolean isTransactional(UseCaseKind kind, Class<?> handlerClass) {
        return TRANSACTION_ATTRIBUTES.hasTransactionAttribute(handleMethodOf(kind), handlerClass);
    }

    private static Method handleMethodOf(UseCaseKind kind) {
        return ClassUtils.getMethod(kind.handlerType(), "handle", kind.messageType());
    }

    private static String capitalized(String word) {
        return Character.toUpperCase(word.charAt(0)) + word.substring(1);
    }
}
