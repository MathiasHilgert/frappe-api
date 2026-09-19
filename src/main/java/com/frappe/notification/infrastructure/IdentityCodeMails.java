package com.frappe.notification.infrastructure;

import com.frappe.identity.AccountExistsMail;
import com.frappe.identity.CodeMail;
import com.frappe.identity.CodeMailer;
import com.frappe.identity.CodePurpose;
import com.frappe.platform.i18n.SupportedLocales;
import com.frappe.platform.mail.MailMessage;
import com.frappe.platform.mail.Mailer;
import java.util.Locale;
import java.util.Map;
import org.springframework.stereotype.Component;

/**
 * notification's side of identity's {@link CodeMailer}: renders identity's one-time codes and the account-exists notice
 * through the {@link Mailer}, in the recipient's language with English as the fallback. The code travels only in memory,
 * from identity's listener into the mail's model; it is never logged here.
 *
 * <p>Code mails carry no idempotency key: every attempt carries a fresh code that replaces the earlier one. The notice's
 * content never changes, so its key ({@code notification/account-exists/<reference>}) lets the provider drop a retry. A
 * transient {@link com.frappe.platform.mail.MailDeliveryException} propagates, so identity's listener fails and the
 * outbox retries it; a permanent rejection is logged by the platform and returns.
 */
@Component
class IdentityCodeMails implements CodeMailer {

    private static final String ACCOUNT_EXISTS = "notification/account-exists";

    private final Mailer mailer;

    /**
     * Creates the adapter.
     *
     * @param mailer the platform's mail port
     */
    IdentityCodeMails(Mailer mailer) {
        this.mailer = mailer;
    }

    @Override
    public void send(CodeMail mail) {
        mailer.send(MailMessage.of(
                templateOf(mail.purpose()),
                mail.recipient(),
                mail.locale(),
                SupportedLocales.FALLBACK,
                Map.of("code", mail.code(), "minutes", mail.validFor().toMinutes())));
    }

    @Override
    public void sendAccountExists(AccountExistsMail mail) {
        mailer.send(MailMessage.of(ACCOUNT_EXISTS, mail.recipient(), mail.locale(), SupportedLocales.FALLBACK, Map.of())
                .withIdempotencyKey(ACCOUNT_EXISTS + "/" + mail.reference()));
    }

    // SIGN_UP -> notification/sign-up-code
    private static String templateOf(CodePurpose purpose) {
        return "notification/" + purpose.name().toLowerCase(Locale.ROOT).replace('_', '-') + "-code";
    }
}
