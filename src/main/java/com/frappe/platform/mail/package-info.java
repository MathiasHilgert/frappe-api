/**
 * Transactional email contracts modules build on: the {@link com.frappe.platform.mail.Mailer} port and the
 * {@link com.frappe.platform.mail.MailMessage} it sends. Sending happens in event listeners after the commit, so the
 * outbox retries a failed delivery.
 */
@NamedInterface("mail")
package com.frappe.platform.mail;

import org.springframework.modulith.NamedInterface;
