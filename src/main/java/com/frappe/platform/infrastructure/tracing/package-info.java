/**
 * W3C trace context across asynchronous hops: the context an event was recorded in is stored with it, travels in its
 * NATS message, and spans on the other side link to it instead of continuing it.
 */
package com.frappe.platform.infrastructure.tracing;
