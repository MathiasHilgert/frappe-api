package com.frappe.platform;

/** Base unit of a measured business value; exporters append it to the metric name. */
public enum MetricUnit {

    /** Plain quantities: guests, items, covers. */
    ITEMS("items"),

    /** Durations; the field is a {@code Duration}, recorded in seconds with histogram buckets. */
    SECONDS("seconds"),

    /** Sizes in bytes. */
    BYTES("bytes"),

    /** Money: the field is a {@code Money}-shaped record, recorded in minor units with a {@code currency} tag. */
    MONEY("minor_units");

    private final String baseUnit;

    MetricUnit(String baseUnit) {
        this.baseUnit = baseUnit;
    }

    /**
     * The unit name exported with the metric.
     *
     * @return the base unit, lowercase
     */
    public String baseUnit() {
        return baseUnit;
    }
}
