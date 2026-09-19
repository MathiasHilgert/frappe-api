package com.frappe.platform.web;

import java.util.UUID;

/**
 * Tells whether a person may enter a business; implemented by the access module. The platform asks it before any route
 * under {@code /v1/businesses/{businessId}/} runs for a person's session, once per request and never cached across
 * requests. Without an implementation nobody is a member, so persons enter no business.
 */
@FunctionalInterface
public interface BusinessMembership {

    /**
     * Tells whether a person belongs to a business.
     *
     * @param personId the person, the principal of a {@link SessionKind#PERSON} session
     * @param businessId the business named by the request path; it may not exist
     * @return {@code true} only when the person is a member of that existing business
     */
    boolean isMember(UUID personId, UUID businessId);
}
