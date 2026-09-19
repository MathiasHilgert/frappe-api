package com.frappe.identity;

import java.util.ArrayList;
import java.util.List;

/**
 * Test double of {@link CodeMailer}: keeps every mail it is asked to send, in order, so identity's tests can read the
 * code a listener handed over without a mail provider.
 */
public final class RecordingCodeMailer implements CodeMailer {

    private final List<CodeMail> codeMails = new ArrayList<>();

    private final List<AccountExistsMail> accountExistsMails = new ArrayList<>();

    @Override
    public synchronized void send(CodeMail mail) {
        codeMails.add(mail);
    }

    @Override
    public synchronized void sendAccountExists(AccountExistsMail mail) {
        accountExistsMails.add(mail);
    }

    /**
     * The code mails sent so far.
     *
     * @return a copy, in sending order
     */
    public synchronized List<CodeMail> codeMails() {
        return List.copyOf(codeMails);
    }

    /**
     * The account-exists notices sent so far.
     *
     * @return a copy, in sending order
     */
    public synchronized List<AccountExistsMail> accountExistsMails() {
        return List.copyOf(accountExistsMails);
    }
}
