package fixtures.bus;

import com.frappe.platform.Command;

/**
 * A command outside every module package. It lives outside {@code com.frappe} on purpose: the bus cannot derive the
 * module of its use case, so a handler for it must fail startup.
 */
public record OutsideModuleCommand() implements Command<String> {}
