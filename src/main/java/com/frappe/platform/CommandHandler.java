package com.frappe.platform;

/**
 * Handles one {@link Command} type: loads the aggregate, calls its behavior, saves it and records its events. Declaring
 * a Spring bean that implements this interface is all it takes; the bus finds it at startup by the command type.
 *
 * <p>The {@code handle} method (or the class) must be {@code @Transactional}, so the saved state and the outbox rows
 * commit together; startup fails otherwise. Expected failures are returned as {@link Result.Failure}, never thrown.
 * Handlers never create telemetry: the bus observes every dispatch.
 *
 * @param <C> the handled command type, a concrete record
 * @param <R> what handling returns, the {@code R} of the command
 */
public interface CommandHandler<C extends Command<R>, R> {

    /**
     * Handles one command.
     *
     * @param command the command, never {@code null}
     * @return the outcome of the use case
     */
    R handle(C command);
}
