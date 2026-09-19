package com.frappe.platform;

/**
 * A request to change state: one record per use case, named for its intent ({@code CloseTab}), dispatched through
 * the {@link CommandBus} to exactly one {@link CommandHandler}.
 *
 * @param <R> what handling the command returns, typically {@code Result<id or small view, ModuleError>}
 */
public interface Command<R> {}
