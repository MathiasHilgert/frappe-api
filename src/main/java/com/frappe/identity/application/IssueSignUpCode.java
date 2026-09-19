package com.frappe.identity.application;

import com.frappe.identity.AccountExistsMail;
import com.frappe.identity.CodeMail;
import com.frappe.identity.CodeMailer;
import com.frappe.identity.CodePurpose;
import com.frappe.identity.IdentitySecrets;
import com.frappe.identity.domain.Persons;
import com.frappe.identity.domain.Secrets;
import com.frappe.identity.domain.SignUpId;
import com.frappe.identity.domain.SignUps;
import com.frappe.platform.CommandUseCase;
import com.frappe.platform.SecretKey;
import com.frappe.platform.ShortLivedSecretStore;
import java.util.UUID;
import org.springframework.transaction.annotation.Transactional;

/**
 * Issues a fresh code for a started sign-up and mails it, replacing any earlier code of the address. The code exists
 * only here, in memory, and hashed in the secret store. The issue cap was counted when the sign-up started, and covers
 * both mails: when the address already belongs to a person, no code is stored and the account-exists notice is sent
 * instead, so only the mailbox holder learns that the address is registered.
 */
@CommandUseCase
public class IssueSignUpCode {

    private final SignUps signUps;
    private final Secrets secrets;
    private final ShortLivedSecretStore store;
    private final CodeMailer mailer;
    private final Persons persons;

    /**
     * Creates the use case; Spring calls it.
     *
     * @param signUps the stored sign-ups
     * @param secrets new codes
     * @param store where the code's hash lives
     * @param mailer delivers the code
     * @param persons the stored people, to tell a registered address
     */
    IssueSignUpCode(SignUps signUps, Secrets secrets, ShortLivedSecretStore store, CodeMailer mailer, Persons persons) {
        this.signUps = signUps;
        this.secrets = secrets;
        this.store = store;
        this.mailer = mailer;
        this.persons = persons;
    }

    /**
     * Stores a new code's hash and mails the code, only after it was stored; or, for a registered address, mails the
     * account-exists notice. A sign-up that no longer exists is skipped.
     *
     * @param signUpId the started sign-up
     * @param reference the id of the event that asked for the mail, the notice's idempotency reference
     */
    @Transactional
    public void issue(SignUpId signUpId, UUID reference) {
        signUps.byId(signUpId).ifPresent(signUp -> {
            if (persons.isRegistered(signUp.email())) {
                mailer.sendAccountExists(new AccountExistsMail(signUp.email().value(), signUp.locale(), reference));
                return;
            }
            var code = secrets.code();
            store.put(SecretKey.of(IdentitySecrets.SIGN_UP, signUp.emailSubject()), code);
            mailer.send(new CodeMail(
                    signUp.email().value(), signUp.locale(), CodePurpose.SIGN_UP, code, IdentitySecrets.SIGN_UP.ttl()));
        });
    }
}
