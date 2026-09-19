package com.frappe.platform.infrastructure.bus;

import com.frappe.platform.Command;
import com.frappe.platform.CommandBus;
import com.frappe.platform.CommandHandler;
import java.util.Objects;

/** Routes a command to its handler bean by the command's exact type; knows nothing about telemetry. */
final class HandlerCommandBus implements CommandBus {

    private final HandlerRegistry handlers;

    /**
     * Creates the bus.
     *
     * @param handlers the command handlers, keyed by command type
     */
    HandlerCommandBus(HandlerRegistry handlers) {
        this.handlers = handlers;
    }

    @Override
    public <R> R dispatch(Command<R> command) {
        Objects.requireNonNull(command, "command must not be null");
        // Safe: the registry keys each handler by the C of CommandHandler<C, R>, and C extends Command<R>.
        @SuppressWarnings("unchecked")
        var handler = (CommandHandler<Command<R>, R>) handlers.handlerFor(command.getClass());
        return handler.handle(command);
    }
}
