package com.frappe.platform.infrastructure.misplaced;

import com.frappe.platform.web.Access;
import com.frappe.platform.web.Posture;
import org.springframework.boot.test.context.TestConfiguration;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.RestController;

/**
 * A route outside an {@code infrastructure.web} package, for the startup check. Nested in a test configuration so
 * component scanning of full application tests never picks it up.
 */
@TestConfiguration(proxyBeanMethods = false)
public class MisplacedRoutes {

    /** A route declared in the wrong package. */
    @RestController
    @Access(Posture.PUBLIC)
    public static class MisplacedRoute {

        /**
         * Answers.
         *
         * @return a constant
         */
        @GetMapping("/misplaced")
        public String answer() {
            return "misplaced";
        }
    }
}
