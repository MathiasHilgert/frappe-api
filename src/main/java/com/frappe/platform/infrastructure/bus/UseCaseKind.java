package com.frappe.platform.infrastructure.bus;

import com.frappe.platform.Command;
import com.frappe.platform.CommandHandler;
import com.frappe.platform.Query;
import com.frappe.platform.QueryHandler;

/** The two kinds of use case, with the types that define each and the value of the {@code use_case.kind} tag. */
enum UseCaseKind {

    /** Changes state; its handler runs in a read-write transaction, rolled back on a failure. */
    COMMAND("command", Command.class, CommandHandler.class, false),

    /** Reads state; its handler runs in a read-only transaction (one snapshot, one tenant setting). */
    QUERY("query", Query.class, QueryHandler.class, true);

    private final String tagValue;
    private final Class<?> messageType;
    private final Class<?> handlerType;
    private final boolean readOnly;

    UseCaseKind(String tagValue, Class<?> messageType, Class<?> handlerType, boolean readOnly) {
        this.tagValue = tagValue;
        this.messageType = messageType;
        this.handlerType = handlerType;
        this.readOnly = readOnly;
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
     * Whether handlers of this kind must declare a read-only transaction; every handler declares a transaction.
     *
     * @return {@code true} for queries
     */
    boolean readOnly() {
        return readOnly;
    }
}
