package com.frappe.platform.infrastructure.metrics;

import java.util.ArrayList;
import java.util.List;
import java.util.Locale;
import java.util.regex.Pattern;

/**
 * The naming and cardinality rules every business metric follows, whether declared by annotation or in code. Collects
 * all problems of one event type so a developer sees them at once.
 */
final class MetricRules {

    /** Tag key added to money distributions. */
    static final String CURRENCY_TAG = "currency";

    private static final String BASE_PACKAGE = "com.frappe.";
    private static final String PREFIX = "frappe.";
    private static final Pattern NAME = Pattern.compile("[a-z][a-z0-9_]*(\\.[a-z][a-z0-9_]*)*");
    private static final Pattern TAG_KEY = Pattern.compile("[a-z][a-z0-9_]*");
    private static final Pattern IDENTIFYING_KEY = Pattern.compile(".*tenant.*|ids?|.*_ids?|.*uuid.*");

    private final Class<?> eventType;
    private final List<String> problems = new ArrayList<>();
    private final String module;

    /**
     * Starts checking the metrics of one event type.
     *
     * @param eventType the event the metrics are declared on
     */
    MetricRules(Class<?> eventType) {
        this.eventType = eventType;
        this.module = moduleOf(eventType);
    }

    /**
     * Checks a metric name and description and returns the full name.
     *
     * @param name name below the module prefix
     * @param description what one recording means
     * @return {@code frappe.<module>.<name>}
     */
    String fullName(String name, String description) {
        if (!NAME.matcher(name).matches()) {
            problems.add("name '" + name + "' must be lowercase words separated by dots, e.g. 'tabs.closed'");
        }
        if (name.startsWith(PREFIX)) {
            problems.add("name '" + name + "' must not start with 'frappe.': the 'frappe.' prefix is added"
                    + " automatically as 'frappe.<module>.'");
        }
        if (name.endsWith("total")) {
            problems.add("name '" + name + "' must not end in 'total': exporters add it to counters");
        }
        if (description.isBlank()) {
            problems.add("metric '" + name + "' needs a description of what one recording means");
        }
        return PREFIX + module + "." + name;
    }

    /**
     * Checks a tag key: snake_case and never an identifier.
     *
     * @param key the tag key
     */
    void tagKey(String key) {
        if (!TAG_KEY.matcher(key).matches()) {
            problems.add("tag '" + key + "' must be lowercase snake_case, e.g. 'payment_method'");
        }
        if (IDENTIFYING_KEY.matcher(key.toLowerCase(Locale.ROOT)).matches()) {
            problems.add("tag '" + key + "' names a tenant or entity id: identifiers have unbounded values and"
                    + " belong on spans as span attributes, never on metrics");
        }
        if (CURRENCY_TAG.equals(key)) {
            problems.add("tag 'currency' is reserved: money distributions add it automatically");
        }
    }

    /**
     * Records a problem found by the caller.
     *
     * @param problem what is wrong and how to fix it
     */
    void reject(String problem) {
        problems.add(problem);
    }

    /**
     * Fails if any rule was broken.
     *
     * @throws InvalidBusinessMetricException listing every problem
     */
    void verify() {
        if (!problems.isEmpty()) {
            throw new InvalidBusinessMetricException(eventType, problems);
        }
    }

    private String moduleOf(Class<?> eventType) {
        var type = eventType.getName();
        if (!type.startsWith(BASE_PACKAGE)) {
            problems.add("the event must live in a module package 'com.frappe.<module>', the metric prefix is derived"
                    + " from it");
            return "unknown";
        }
        var rest = type.substring(BASE_PACKAGE.length());
        return rest.substring(0, rest.indexOf('.'));
    }
}
