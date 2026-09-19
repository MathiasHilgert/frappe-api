/**
 * Notification: what the platform tells people by mail, in their language. It owns content, languages and sending,
 * never the facts behind them: identity's one-time codes arrive in memory through identity's
 * {@link com.frappe.identity.CodeMailer} SPI, which this module implements, so a code never enters an event.
 *
 * <p>notification depends on identity's root package and the platform only; identity never depends on notification,
 * because notification reads contacts through identity and a dependency back would be a cycle. Declared here, so
 * {@code ModularityTests} fails on a violation.
 */
@ApplicationModule(allowedDependencies = {"platform", "platform :: *", "identity"})
package com.frappe.notification;

import org.springframework.modulith.ApplicationModule;
