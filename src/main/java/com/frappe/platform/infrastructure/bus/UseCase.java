package com.frappe.platform.infrastructure.bus;

import java.util.Optional;

/**
 * A use case as telemetry names it, derived from its message type. All three values are bounded by the code base, so
 * they are safe as metric tags.
 *
 * @param kind command or query
 * @param module the module the message belongs to ({@code com.frappe.<module>…}), {@value #UNKNOWN_MODULE} for a
 *     message outside every module (only reachable for a message without a handler, which fails anyway)
 * @param name the simple name of the message type, e.g. {@code CloseTab}
 */
record UseCase(UseCaseKind kind, String module, String name) {

    /** Module value of a message outside every module package. */
    static final String UNKNOWN_MODULE = "unknown";

    private static final String BASE_PACKAGE = "com.frappe.";

    /**
     * Describes the use case of a message type.
     *
     * @param kind command or query
     * @param messageType the message type
     * @return the use case
     */
    static UseCase of(UseCaseKind kind, Class<?> messageType) {
        return new UseCase(kind, moduleOf(messageType).orElse(UNKNOWN_MODULE), messageType.getSimpleName());
    }

    /**
     * The module of a message type: the package segment after {@code com.frappe}.
     *
     * @param messageType the message type
     * @return the module, or empty when the type is not inside a module package
     */
    static Optional<String> moduleOf(Class<?> messageType) {
        var packageName = messageType.getPackageName() + ".";
        if (!packageName.startsWith(BASE_PACKAGE)) {
            return Optional.empty();
        }
        var rest = packageName.substring(BASE_PACKAGE.length());
        var moduleEnd = rest.indexOf('.');
        return moduleEnd <= 0 ? Optional.empty() : Optional.of(rest.substring(0, moduleEnd));
    }
}
