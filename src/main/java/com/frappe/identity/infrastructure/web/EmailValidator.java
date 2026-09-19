package com.frappe.identity.infrastructure.web;

import com.frappe.identity.domain.EmailAddress;
import com.frappe.platform.Result;
import jakarta.validation.ConstraintValidator;
import jakarta.validation.ConstraintValidatorContext;

/** Checks {@link Email} with the domain's own rule, so the route accepts exactly what the domain accepts. */
class EmailValidator implements ConstraintValidator<Email, String> {

    /** Created by Bean Validation. */
    EmailValidator() {}

    @Override
    public boolean isValid(String value, ConstraintValidatorContext context) {
        return value == null || EmailAddress.of(value) instanceof Result.Success;
    }
}
