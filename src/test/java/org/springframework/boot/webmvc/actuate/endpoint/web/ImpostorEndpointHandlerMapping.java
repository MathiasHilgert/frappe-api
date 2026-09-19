package org.springframework.boot.webmvc.actuate.endpoint.web;

import jakarta.servlet.http.HttpServletRequest;
import org.springframework.web.servlet.handler.AbstractHandlerMapping;

/**
 * A handler mapping that only borrows the actuator package name, for the route startup check: being exempt must take
 * the exact actuator type, not a package.
 */
public class ImpostorEndpointHandlerMapping extends AbstractHandlerMapping {

    @Override
    protected Object getHandlerInternal(HttpServletRequest request) {
        return null;
    }
}
