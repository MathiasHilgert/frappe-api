package com.frappe.platform.infrastructure.bus;

/**
 * A command or query was sent for which no handler is declared: a programming error, found by the first test that
 * sends it.
 */
class MissingHandlerException extends RuntimeException {

    private static final long serialVersionUID = 1L;

    /**
     * Creates the exception naming the message type and how to fix it.
     *
     * @param kind command or query
     * @param messageType the type nobody handles
     */
    MissingHandlerException(UseCaseKind kind, Class<?> messageType) {
        super("No " + kind.tagValue() + " handler for " + messageType.getName() + "; declare a bean implementing "
                + kind.handlerType().getSimpleName() + "<" + messageType.getSimpleName() + ", R>");
    }
}
