package com.frappe.platform.infrastructure.web;

import java.util.List;

/** Routes break the route rules; thrown at startup so the application refuses to start. */
class InvalidRouteException extends RuntimeException {

    private static final long serialVersionUID = 1L;

    /**
     * Creates the exception listing every problem found.
     *
     * @param problems what is wrong, each naming the route class and the fix
     */
    InvalidRouteException(List<String> problems) {
        super("Invalid HTTP routes:\n  - " + String.join("\n  - ", problems));
    }
}
