/**
 * Transactional outbox: the {@link com.frappe.platform.DomainEventPublisher} adapter over Spring Modulith's JDBC event
 * publication registry ({@code platform.event_publication}), and the recovery of publications that did not complete.
 */
package com.frappe.platform.infrastructure.events;
