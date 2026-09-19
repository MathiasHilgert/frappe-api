package com.frappe.platform.infrastructure;

/**
 * Application event: a messaging transport became available again (connected or reconnected). The outbox recovery
 * listens to it and resubmits failed publications at once, so the transport adapter needs no dependency on the
 * outbox. Internal to the platform module; not an outbox event and never stored.
 */
public enum MessagingTransportRecovered {

    /** The NATS connection was (re)established and the stream provisioned. */
    NATS
}
