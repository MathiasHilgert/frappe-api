package com.frappe.identity.application;

import com.frappe.identity.CodeMail;
import com.frappe.identity.CodeMailer;
import com.frappe.identity.CodePurpose;
import com.frappe.identity.IdentitySecrets;
import com.frappe.identity.domain.Secrets;
import com.frappe.identity.domain.SignUpId;
import com.frappe.identity.domain.SignUps;
import com.frappe.platform.CommandUseCase;
import com.frappe.platform.SecretKey;
import com.frappe.platform.ShortLivedSecretStore;
import org.springframework.transaction.annotation.Transactional;

/**
 * Issues a fresh code for a started sign-up and mails it, replacing any earlier code of the address. The code exists
 * only here, in memory, and hashed in the secret store. The issue cap was counted when the sign-up started.
 */
@CommandUseCase
public class IssueSignUpCode {

    private final SignUps signUps;
    private final Secrets secrets;
    private final ShortLivedSecretStore store;
    private final CodeMailer mailer;

    /**
     * Creates the use case; Spring calls it.
     *
     * @param signUps the stored sign-ups
     * @param secrets new codes
     * @param store where the code's hash lives
     * @param mailer delivers the code
     */
    IssueSignUpCode(SignUps signUps, Secrets secrets, ShortLivedSecretStore store, CodeMailer mailer) {
        this.signUps = signUps;
        this.secrets = secrets;
        this.store = store;
        this.mailer = mailer;
    }

    /**
     * Stores a new code's hash and mails the code, only after it was stored. A sign-up that no longer exists is skipped.
     *
     * @param signUpId the started sign-up
     */
    @Transactional
    public void issue(SignUpId signUpId) {
        signUps.byId(signUpId).ifPresent(signUp -> {
            var code = secrets.code();
            store.put(SecretKey.of(IdentitySecrets.SIGN_UP, signUp.emailSubject()), code);
            mailer.send(new CodeMail(
                    signUp.email().value(), signUp.locale(), CodePurpose.SIGN_UP, code, IdentitySecrets.SIGN_UP.ttl()));
        });
    }
}
