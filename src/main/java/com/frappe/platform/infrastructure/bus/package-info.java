/**
 * The command and query buses: handlers discovered from Spring beans at startup, one per message type, and every
 * dispatch observed as a {@code use_case} observation around the handler's transaction.
 */
package com.frappe.platform.infrastructure.bus;
