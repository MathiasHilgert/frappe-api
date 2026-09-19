package com.frappe.platform.infrastructure.bus;

import java.util.List;

/** The declared handlers break the bus rules; thrown at startup so the application refuses to start. */
class InvalidHandlersException extends RuntimeException {

    private static final long serialVersionUID = 1L;

    /**
     * Creates the exception listing every problem found among the handlers of one kind.
     *
     * @param kind command or query
     * @param problems what is wrong, each naming the beans involved and the fix
     */
    InvalidHandlersException(UseCaseKind kind, List<String> problems) {
        super("Invalid " + kind.tagValue() + " handlers:\n  - " + String.join("\n  - ", problems));
    }
}
