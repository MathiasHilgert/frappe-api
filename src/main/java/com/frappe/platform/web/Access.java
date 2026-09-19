package com.frappe.platform.web;

import java.lang.annotation.Documented;
import java.lang.annotation.ElementType;
import java.lang.annotation.Retention;
import java.lang.annotation.RetentionPolicy;
import java.lang.annotation.Target;

/**
 * Declares whether a route needs an authenticated caller. Mandatory on every route class (a {@code @RestController}
 * with exactly one mapped method); the application refuses to start while a route lacks it, so no route becomes
 * public by accident. Authorization is not declared here: the use case decides it (see {@link Posture}).
 *
 * <pre>{@code
 * @RestController
 * @Access(Posture.AUTHENTICATED)
 * class RevokeCurrentSession { ... }
 * }</pre>
 */
@Documented
@Target(ElementType.TYPE)
@Retention(RetentionPolicy.RUNTIME)
public @interface Access {

    /**
     * The posture enforced before the route runs.
     *
     * @return the posture
     */
    Posture value();
}
