package com.frappe.platform.infrastructure.web;

import com.frappe.platform.web.Access;
import com.frappe.platform.web.Posture;
import jakarta.validation.Valid;
import jakarta.validation.constraints.Min;
import jakarta.validation.constraints.NotBlank;
import jakarta.validation.constraints.NotNull;
import jakarta.validation.constraints.Size;
import java.util.List;
import org.springframework.boot.test.context.TestConfiguration;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RestController;

/** Routes for the problem detail tests; importing this registers its member route classes. */
@TestConfiguration(proxyBeanMethods = false)
public class ProblemRoutes {

    /** The validated route's path, with the {@code /v1} prefix. */
    public static final String ORDERS_PATH = "/v1/test/problems/orders";

    /** A request body with constraints on its fields, a nested list and its elements. */
    record OrderRequest(
            @NotBlank String name,
            @Size(min = 2, max = 5) String code,
            @NotNull @Valid List<Line> lines) {}

    /** One line of an order. */
    record Line(@Min(1) int quantity) {}

    /** Accepts a valid order. */
    @RestController
    @Access(Posture.PUBLIC)
    static class OrdersRoute {

        @PostMapping("/test/problems/orders")
        String place(@Valid @RequestBody OrderRequest order) {
            return "placed " + order.name();
        }
    }

    /** Only answers GET, so other methods are not allowed. */
    @RestController
    @Access(Posture.PUBLIC)
    static class ReadOnlyRoute {

        @GetMapping("/test/problems/read-only")
        String read() {
            return "read";
        }
    }
}
