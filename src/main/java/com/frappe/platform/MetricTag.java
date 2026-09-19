package com.frappe.platform;

import java.lang.annotation.Documented;
import java.lang.annotation.Retention;
import java.lang.annotation.RetentionPolicy;
import java.lang.annotation.Target;

/**
 * One dimension of a business metric, read from an enum or boolean field of the event, so its values are bounded by
 * construction. Identifiers (tenant, branch, aggregate ids) are never tags: they belong on spans.
 */
@Documented
@Retention(RetentionPolicy.RUNTIME)
@Target({})
public @interface MetricTag {

    /**
     * Tag key: lowercase snake_case ({@code channel}, {@code payment_method}).
     *
     * @return the key
     */
    String key();

    /**
     * Name of the enum or boolean event field holding the value; enum values are written in lowercase.
     *
     * @return the field name
     */
    String from();
}
