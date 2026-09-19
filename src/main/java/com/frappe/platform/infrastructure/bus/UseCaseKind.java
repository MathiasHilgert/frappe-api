package com.frappe.platform.infrastructure.bus;

import com.frappe.platform.Command;
import com.frappe.platform.CommandHandler;
import com.frappe.platform.Query;
import com.frappe.platform.QueryHandler;

/** The two kinds of use case, with the types that define each and the value of the {@code use_case.kind} tag. */
enum UseCaseKind {

    /** Changes state; its handler must be transactional. */
    COMMAND("command", Command.class, CommandHandler.class, true),

    /** Reads state; its handler is not transactional. */
    QUERY("query", Query.class, QueryHandler.class, false);

    private final String tagValue;
    private final Class<?> messageType;
    private final Class<?> handlerType;
    private final boolean transactional;

    UseCaseKind(String tagValue, Class<?> messageType, Class<?> handlerType, boolean transactional) {
        this.tagValue = tagValue;
        this.messageType = messageType;
        this.handlerType = handlerType;
        this.transactional = transactional;
    }

    /**
     * The value of the {@code use_case.kind} tag and the word used in messages.
     *
     * @return {@code command} or {@code query}
     */
    String tagValue() {
        return tagValue;
    }

    /**
     * The marker interface of the messages of this kind.
     *
     * @return {@link Command} or {@link Query}
     */
    Class<?> messageType() {
        return messageType;
    }

    /**
     * The interface the handlers of this kind implement.
     *
     * @return {@link CommandHandler} or {@link QueryHandler}
     */
    Class<?> handlerType() {
        return handlerType;
    }

    /**
     * Whether handlers of this kind must run in a transaction of their own.
     *
     * @return {@code true} for commands
     */
    boolean transactional() {
        return transactional;
    }
}
