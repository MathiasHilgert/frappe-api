package com.frappe.identity;

/**
 * SPI identity requires and notification implements: delivers identity's one-time codes and the account-exists notice.
 * identity keeps the code's lifecycle (generate, hash, cap, check); the implementation owns content, language and
 * sending. A transient delivery failure propagates, so identity's listener fails and the outbox retries it.
 */
public interface CodeMailer {

    /**
     * Sends a one-time code.
     *
     * @param mail the code and its recipient
     */
    void send(CodeMail mail);

    /**
     * Sends the notice that the address already has an account.
     *
     * @param mail the recipient and the attempt's reference
     */
    void sendAccountExists(AccountExistsMail mail);
}
