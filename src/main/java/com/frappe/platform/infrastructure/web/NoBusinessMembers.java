package com.frappe.platform.infrastructure.web;

import com.frappe.platform.web.BusinessMembership;
import java.util.UUID;

/** The membership port until the access module provides one: nobody is a member of any business. */
final class NoBusinessMembers implements BusinessMembership {

    /** Creates the port. */
    NoBusinessMembers() {}

    @Override
    public boolean isMember(UUID personId, UUID businessId) {
        return false;
    }
}
