package com.frappe.platform;

/**
 * Dispatches a {@link Command} to its single {@link CommandHandler} and returns what the handler returned, unchanged.
 * Every dispatch is observed (span and {@code use_case} timer) around the handler's transaction.
 */
public interface CommandBus {

    /**
     * Runs the use case of the command in the calling thread.
     *
     * @param command the command, not {@code null}
     * @param <R> the result type the command declares
     * @return the result of its handler
     * @throws NullPointerException if the command is {@code null}
     * @throws RuntimeException the handler's own exception, or a {@code MissingHandlerException} when no handler is
     *     declared for the command type: a programming error found by the first test that dispatches the command,
     *     never an outcome to catch and handle
     */
    <R> R dispatch(Command<R> command);
}
