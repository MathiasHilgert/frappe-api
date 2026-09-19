package com.frappe.platform.infrastructure.usecase;

import java.util.Optional;

/**
 * A use case as telemetry names it, derived from its class. All three values are bounded by the code base, so they are
 * safe as metric tags.
 *
 * @param kind command or query
 * @param module the module the class belongs to ({@code com.frappe.<module>…}), {@value #UNKNOWN_MODULE} for a class
 *     outside every module package
 * @param name the simple name of the class, e.g. {@code CloseTab}
 */
record UseCase(UseCaseKind kind, String module, String name) {

    /** Module value of a class outside every module package. */
    static final String UNKNOWN_MODULE = "unknown";

    private static final String BASE_PACKAGE = "com.frappe.";

    /**
     * Describes the use case implemented by a class.
     *
     * @param kind command or query
     * @param useCaseClass the use case's class (not its proxy)
     * @return the use case
     */
    static UseCase of(UseCaseKind kind, Class<?> useCaseClass) {
        return new UseCase(kind, moduleOf(useCaseClass).orElse(UNKNOWN_MODULE), useCaseClass.getSimpleName());
    }

    /**
     * The module of a type: the package segment after {@code com.frappe}.
     *
     * @param type the use case class
     * @return the module, or empty when the type is not inside a module package
     */
    static Optional<String> moduleOf(Class<?> type) {
        var packageName = type.getPackageName() + ".";
        if (!packageName.startsWith(BASE_PACKAGE)) {
            return Optional.empty();
        }
        var rest = packageName.substring(BASE_PACKAGE.length());
        var moduleEnd = rest.indexOf('.');
        return moduleEnd <= 0 ? Optional.empty() : Optional.of(rest.substring(0, moduleEnd));
    }
}
