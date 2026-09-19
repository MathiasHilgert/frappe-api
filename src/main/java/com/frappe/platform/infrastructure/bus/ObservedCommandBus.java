package com.frappe.platform.infrastructure.bus;

import com.frappe.platform.Command;
import com.frappe.platform.CommandBus;
import java.util.Objects;

/** Decorates a command bus with one {@code use_case} observation per dispatch; handlers never create telemetry. */
final class ObservedCommandBus implements CommandBus {

    private final CommandBus delegate;
    private final UseCaseObservations observations;

    /**
     * Creates the decorator.
     *
     * @param delegate the bus that runs the handler
     * @param observations records the dispatch
     */
    ObservedCommandBus(CommandBus delegate, UseCaseObservations observations) {
        this.delegate = delegate;
        this.observations = observations;
    }

    @Override
    public <R> R dispatch(Command<R> command) {
        Objects.requireNonNull(command, "command must not be null");
        return observations.observe(UseCaseKind.COMMAND, command, () -> delegate.dispatch(command));
    }
}
