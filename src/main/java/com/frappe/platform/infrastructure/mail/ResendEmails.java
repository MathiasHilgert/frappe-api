package com.frappe.platform.infrastructure.mail;

import com.resend.core.exception.ResendException;
import com.resend.core.net.RequestOptions;
import com.resend.services.emails.model.CreateEmailOptions;
import com.resend.services.emails.model.CreateEmailResponse;

/**
 * The one Resend SDK call the transport makes ({@code Resend#emails()#send}). A seam because the SDK's base URL is fixed
 * to {@code https://api.resend.com}: tests stub the provider here and never call the real API.
 */
@FunctionalInterface
interface ResendEmails {

    /**
     * Sends one email.
     *
     * @param options the email
     * @param request request options, carrying the idempotency key
     * @return Resend's response
     * @throws ResendException if Resend answered with an error status
     */
    CreateEmailResponse send(CreateEmailOptions options, RequestOptions request) throws ResendException;
}
