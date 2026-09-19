package com.frappe.platform.infrastructure.web;

import static org.assertj.core.api.Assertions.assertThat;

import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.support.DefaultListableBeanFactory;
import org.springframework.mock.web.MockHttpServletRequest;
import org.springframework.web.servlet.function.RouterFunctions;
import org.springframework.web.servlet.function.ServerResponse;
import org.springframework.web.servlet.function.support.RouterFunctionMapping;
import org.springframework.web.servlet.mvc.method.annotation.RequestMappingHandlerMapping;

/** Defense in depth: a request no route serves passes through only when no other handler mapping serves it either. */
class HandlerMappingGuardTest {

    @Test
    void aHandlerAddedToAnAllowedMappingOutsideTheStartupCheckIsStillSeen() throws Exception {
        // Given a functional route set in code (no RouterFunction bean, so the startup check cannot see it)
        var functional = new RouterFunctionMapping();
        functional.setRouterFunction(RouterFunctions.route()
                .GET("/v1/fn/secret", request -> ServerResponse.ok().body("secret"))
                .build());
        functional.afterPropertiesSet();
        var guard = guardWith(functional);

        // Then its path counts as served, and an unknown path does not
        assertThat(guard.servesOutsideRoutes(new MockHttpServletRequest("GET", "/v1/fn/secret")))
                .isTrue();
        assertThat(guard.servesOutsideRoutes(new MockHttpServletRequest("GET", "/v1/unknown")))
                .isFalse();
    }

    @Test
    void everyRequestCountsAsServedUntilTheMappingsWereChecked() {
        var guard = new HandlerMappingGuard(new DefaultListableBeanFactory(), new RequestMappingHandlerMapping());

        assertThat(guard.servesOutsideRoutes(new MockHttpServletRequest("GET", "/v1/unknown")))
                .isTrue();
    }

    private static HandlerMappingGuard guardWith(RouterFunctionMapping functional) {
        var beans = new DefaultListableBeanFactory();
        beans.registerSingleton("routerFunctionMapping", functional);
        var guard = new HandlerMappingGuard(beans, new RequestMappingHandlerMapping());
        guard.afterSingletonsInstantiated();
        return guard;
    }
}
