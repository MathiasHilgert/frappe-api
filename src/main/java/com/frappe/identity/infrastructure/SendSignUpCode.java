package com.frappe.identity.infrastructure;

import com.frappe.identity.application.IssueSignUpCode;
import com.frappe.identity.domain.SignUpId;
import com.frappe.identity.domain.SignUpStarted;
import org.springframework.modulith.events.ApplicationModuleListener;
import org.springframework.stereotype.Component;

/**
 * Mails the code of a started sign-up after its transaction committed. A failure (mail provider down) leaves the
 * publication incomplete, and the outbox retries it with a fresh code; the issue cap was already counted in the request,
 * so a retry never spends it.
 */
@Component
class SendSignUpCode {

    private final IssueSignUpCode issueSignUpCode;

    /**
     * Creates the listener.
     *
     * @param issueSignUpCode the use case issuing and mailing the code
     */
    SendSignUpCode(IssueSignUpCode issueSignUpCode) {
        this.issueSignUpCode = issueSignUpCode;
    }

    /**
     * Issues and mails the code.
     *
     * @param event the started sign-up
     */
    @ApplicationModuleListener
    void on(SignUpStarted event) {
        issueSignUpCode.issue(new SignUpId(event.aggregateId()));
    }
}
