package com.frappe.platform.web;

import java.lang.annotation.Documented;
import java.lang.annotation.ElementType;
import java.lang.annotation.Retention;
import java.lang.annotation.RetentionPolicy;
import java.lang.annotation.Target;

/**
 * Declares who may call a route. Mandatory on every route class (a {@code @RestController} with exactly one mapped
 * method); the application refuses to start while a route lacks it, so no route becomes public by accident.
 *
 * <pre>{@code
 * @RestController
 * @Access(Posture.AUTHENTICATED)
 * class RevokeCurrentSession { ... }
 *
 * @RestController
 * @Access(value = Posture.PERMISSION, permission = "tabs.close")
 * class CloseTab { ... }
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

    /**
     * The permission the caller needs; required for {@link Posture#PERMISSION} and not allowed for any other posture.
     *
     * @return the permission name, empty when the posture checks none
     */
    String permission() default "";
}
