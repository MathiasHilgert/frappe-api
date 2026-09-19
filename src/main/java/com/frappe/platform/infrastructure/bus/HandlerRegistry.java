package com.frappe.platform.infrastructure.bus;

import java.lang.reflect.Modifier;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Objects;
import java.util.Optional;
import java.util.stream.Collectors;
import java.util.stream.Stream;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.beans.factory.config.ConfigurableListableBeanFactory;
import org.springframework.core.ResolvableType;
import org.springframework.transaction.annotation.AnnotationTransactionAttributeSource;
import org.springframework.transaction.interceptor.TransactionAttributeSource;
import org.springframework.util.ClassUtils;

/**
 * The handler beans of one kind, keyed by the message type they handle. Built once at startup from the bean
 * definitions, without instantiating a handler (a handler may itself depend on a bus, e.g. through another module's
 * API): every rule violation is collected and reported together, so a routing gap stops the application instead of
 * reaching a customer.
 */
final class HandlerRegistry {

    private static final Logger log = LoggerFactory.getLogger(HandlerRegistry.class);

    // The rule Spring's transaction proxy itself applies (public methods, method before class, bridge methods
    // resolved), so the startup check can never disagree with what happens at runtime.
    private static final TransactionAttributeSource TRANSACTION_ATTRIBUTES = new AnnotationTransactionAttributeSource();

    private final UseCaseKind kind;
    private final ConfigurableListableBeanFactory beans;
    private final Map<Class<?>, String> beanNamesByMessageType;

    private HandlerRegistry(
            UseCaseKind kind, ConfigurableListableBeanFactory beans, Map<Class<?>, String> beanNamesByMessageType) {
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
    static HandlerRegistry discover(ConfigurableListableBeanFactory beans, UseCaseKind kind) {
        var beanNamesByType = new LinkedHashMap<Class<?>, List<String>>();
        var problems = new ArrayList<String>();
        for (var beanName : beans.getBeanNamesForType(kind.handlerType())) {
            var declaration = declarationOf(beans, beanName, kind);
            if (declaration.isEmpty()) {
                problems.add(unresolvableProblem(kind, beanName));
                continue;
            }
            var messageType = declaration.get().messageType();
            if (UseCase.moduleOf(messageType).isEmpty()) {
                problems.add(messageType.getName() + " (handled by '" + beanName + "') must live in a module package"
                        + " 'com.frappe.<module>'; the module of its use case is derived from it");
            }
            if (kind.transactional() && !isTransactional(kind, declaration.get().handlerClass())) {
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

    /**
     * What a handler bean was declared as: its class and the concrete message type it handles.
     *
     * @param handlerClass the declared class of the handler
     * @param messageType the exact message class it handles
     */
    private record Declaration(Class<?> handlerClass, Class<?> messageType) {}

    // The bean definition is asked first: it knows the declared class even when the bean already is a JDK proxy,
    // which implements the handler interface raw. The bean type is the fallback for beans without a definition; a
    // CGLIB subclass is mapped back to its user class, which carries the generics.
    private static Optional<Declaration> declarationOf(
            ConfigurableListableBeanFactory beans, String beanName, UseCaseKind kind) {
        var declaredClass = beans.containsBeanDefinition(beanName)
                ? beans.getMergedBeanDefinition(beanName).getResolvableType().resolve()
                : null;
        var beanType = beans.getType(beanName);
        var beanClass = beanType == null ? null : ClassUtils.getUserClass(beanType);
        return Stream.of(declaredClass, beanClass)
                .filter(Objects::nonNull)
                .flatMap(handlerClass -> messageTypeOf(kind, handlerClass).stream()
                        .map(messageType -> new Declaration(handlerClass, messageType)))
                .findFirst();
    }

    private static Optional<Class<?>> messageTypeOf(UseCaseKind kind, Class<?> handlerClass) {
        // An unresolved type variable (lambda, generic handler class) resolves to its bound, the marker interface;
        // routing is by exact message class, so only a concrete class is a valid key.
        var messageType = ResolvableType.forClass(kind.handlerType(), handlerClass)
                .getGeneric(0)
                .resolve();
        if (messageType == null || messageType.isInterface() || Modifier.isAbstract(messageType.getModifiers())) {
            return Optional.empty();
        }
        return Optional.of(messageType);
    }

    private static boolean isTransactional(UseCaseKind kind, Class<?> handlerClass) {
        var handle = ClassUtils.getMethod(kind.handlerType(), "handle", kind.messageType());
        return TRANSACTION_ATTRIBUTES.hasTransactionAttribute(handle, handlerClass);
    }

    private static String unresolvableProblem(UseCaseKind kind, String beanName) {
        var word = kind.tagValue();
        return "cannot tell which " + word + " bean '" + beanName + "' handles; declare it as a class implementing "
                + kind.handlerType().getSimpleName() + "<Your" + Character.toUpperCase(word.charAt(0))
                + word.substring(1) + ", YourResult> with a concrete " + word + " record";
    }
}
