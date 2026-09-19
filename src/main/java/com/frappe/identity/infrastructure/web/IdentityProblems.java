package com.frappe.identity.infrastructure.web;

import com.frappe.identity.application.IdentityRefusal;
import com.frappe.platform.web.Problem;
import com.frappe.platform.web.ProblemMapper;
import java.util.Locale;
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
            case IdentityRefusal.InvalidCode _ -> Problem.of(400, "invalid-code", "identity.problem.invalid-code");
            case IdentityRefusal.PasswordRefused refused ->
                Problem.of(422, "password-rejected", "identity.problem.password-rejected")
                        .with("reason", refused.reason().name().toLowerCase(Locale.ROOT));
            case IdentityRefusal.InvalidName _ -> Problem.of(422, "invalid-name", "identity.problem.invalid-name");
            case IdentityRefusal.LegalTermsOutdated _ ->
                Problem.of(409, "legal-terms-outdated", "identity.problem.legal-terms-outdated");
            case IdentityRefusal.EmailAlreadyRegistered _ ->
                Problem.of(409, "email-already-registered", "identity.problem.email-already-registered");
        };
    }
}
