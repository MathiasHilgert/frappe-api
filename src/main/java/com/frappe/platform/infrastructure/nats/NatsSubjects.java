package com.frappe.platform.infrastructure.nats;

import com.frappe.platform.DomainEvent;
import java.util.Locale;
import java.util.regex.Pattern;

/** Naming contract: subject {@code frappe.<module>.<event-kebab>.v<eventVersion>}, type {@code <module>.<event-kebab>}. */
final class NatsSubjects {

    /** First token of every Frappé subject. */
    static final String PREFIX = "frappe";

    private static final String BASE_PACKAGE = "com.frappe.";
    private static final Pattern WORD_BOUNDARY = Pattern.compile("(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])");

    private NatsSubjects() {}

    /**
     * The subject an event is published on.
     *
     * @param event the event
     * @return {@code frappe.<module>.<event-kebab>.v<eventVersion>}
     */
    static String of(DomainEvent event) {
        return PREFIX + "." + eventType(event.getClass()) + ".v" + event.eventVersion();
    }

    /**
     * The stable type name of an event class, used in the subject and the {@code Frappe-Event-Type} header.
     *
     * @param type the event class
     * @return {@code <module>.<event-kebab>}
     * @throws IllegalArgumentException if the class is outside {@code com.frappe.<module>}
     */
    static String eventType(Class<?> type) {
        var packageName = type.getPackageName();
        if (!packageName.startsWith(BASE_PACKAGE)) {
            throw new IllegalArgumentException("Cannot derive a NATS subject for " + type.getName()
                    + ": externalized events must live in package com.frappe.<module>");
        }
        var module = packageName.substring(BASE_PACKAGE.length()).split("\\.")[0];
        var name = WORD_BOUNDARY.matcher(type.getSimpleName()).replaceAll("-").toLowerCase(Locale.ROOT);
        return module + "." + name;
    }
}
