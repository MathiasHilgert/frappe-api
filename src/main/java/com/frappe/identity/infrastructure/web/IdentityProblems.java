package com.frappe.identity.infrastructure.web;

import com.frappe.identity.application.IdentityRefusal;
import com.frappe.platform.web.Problem;
import com.frappe.platform.web.ProblemMapper;
import org.springframework.stereotype.Component;

/** The problems identity's refusals are answered with. */
@Component
class IdentityProblems implements ProblemMapper<IdentityRefusal> {

    /** Created by Spring. */
    IdentityProblems() {}

    @Override
    public Class<IdentityRefusal> failureType() {
        return IdentityRefusal.class;
    }

    @Override
    public Problem problemOf(IdentityRefusal refusal) {
        return switch (refusal) {
            case IdentityRefusal.TooManyAttempts _ ->
                Problem.of(429, "too-many-attempts", "identity.problem.too-many-attempts");
        };
    }
}
