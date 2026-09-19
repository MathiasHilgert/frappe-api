package com.frappe.identity.infrastructure.web;

import jakarta.validation.Constraint;
import jakarta.validation.Payload;
import java.lang.annotation.Documented;
import java.lang.annotation.ElementType;
import java.lang.annotation.Retention;
import java.lang.annotation.RetentionPolicy;
import java.lang.annotation.Target;

/**
 * The value is an email address identity accepts ({@link com.frappe.identity.domain.EmailAddress#of(String)}): at most
 * 254 octets of UTF-8 with exactly one {@code @}, not at either end. {@code null} is valid; add {@code @NotNull}.
 * Answered with the validation code {@code email}.
 */
@Documented
@Constraint(validatedBy = EmailValidator.class)
@Target({ElementType.FIELD, ElementType.RECORD_COMPONENT, ElementType.PARAMETER})
@Retention(RetentionPolicy.RUNTIME)
@interface Email {

    /**
     * The message template.
     *
     * @return the template
     */
    String message() default "must be an email address";

    /**
     * The validation groups.
     *
     * @return the groups
     */
    Class<?>[] groups() default {};

    /**
     * The payload.
     *
     * @return the payload
     */
    Class<? extends Payload>[] payload() default {};
}
