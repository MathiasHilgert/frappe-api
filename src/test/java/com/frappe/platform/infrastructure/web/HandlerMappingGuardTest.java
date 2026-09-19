package com.frappe.platform.infrastructure.web;

import static org.assertj.core.api.Assertions.assertThat;

import java.util.List;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.support.DefaultListableBeanFactory;
import org.springframework.http.converter.StringHttpMessageConverter;
import org.springframework.mock.web.MockHttpServletRequest;
import org.springframework.web.servlet.function.RouterFunctions;
import org.springframework.web.servlet.function.ServerResponse;
import org.springframework.web.servlet.function.support.RouterFunctionMapping;
import org.springframework.web.servlet.mvc.method.annotation.RequestMappingHandlerMapping;

/**
 * Defense in depth: handlers that appear after the startup check (a router function fed to Spring MVC's own
 * functional mapping in code) never serve a request without a route's posture.
 */
class HandlerMappingGuardTest {

    @Test
    void aHandlerFedAfterStartupServesNoPathNoRouteMatches() {
        // Given Spring MVC's own functional mapping, empty at startup
        var functional = emptyFunctionalMapping();
        var guard = guardWith(functional);

        // When code feeds it a route after the startup check
        functional.setRouterFunction(RouterFunctions.route()
                .GET("/v1/fn/secret", request -> ServerResponse.ok().body("secret"))
                .build());

        // Then its path counts as served, and an unknown path does not
        assertThat(guard.servesOutsideRoutes(new MockHttpServletRequest("GET", "/v1/fn/secret")))
                .isTrue();
        assertThat(guard.servesOutsideRoutes(new MockHttpServletRequest("GET", "/v1/unknown")))
                .isFalse();
    }

    @Test
    void aMappingOrderedBeforeTheRoutesThatGainsRoutesAfterStartupShadowsThem() {
        // Given Spring MVC's own functional mapping (ordered before the annotated routes), empty at startup
        var functional = emptyFunctionalMapping();
        var guard = guardWith(functional);

        // When code feeds it the path of an annotated route after the startup check
        functional.setRouterFunction(RouterFunctions.route()
                .GET("/v1/test/public", request -> ServerResponse.ok().body("shadow"))
                .build());

        // Then a request for that path counts as shadowed, another path does not
        assertThat(guard.shadowsRoutes(new MockHttpServletRequest("GET", "/v1/test/public")))
                .isTrue();
        assertThat(guard.shadowsRoutes(new MockHttpServletRequest("GET", "/v1/test/other")))
                .isFalse();
    }

    @Test
    void everyRequestCountsAsServedUntilTheMappingsWereChecked() {
        var guard = new HandlerMappingGuard(new DefaultListableBeanFactory(), new RequestMappingHandlerMapping());

        assertThat(guard.servesOutsideRoutes(new MockHttpServletRequest("GET", "/v1/unknown")))
                .isTrue();
        assertThat(guard.shadowsRoutes(new MockHttpServletRequest("GET", "/v1/unknown")))
                .isTrue();
    }

    private static RouterFunctionMapping emptyFunctionalMapping() {
        // As WebMvcConfigurationSupport#routerFunctionMapping: order -1, before RequestMappingHandlerMapping (0)
        var functional = new RouterFunctionMapping();
        functional.setOrder(-1);
        functional.setMessageConverters(List.of(new StringHttpMessageConverter()));
        return functional;
    }

    private static HandlerMappingGuard guardWith(RouterFunctionMapping functional) {
        var beans = new DefaultListableBeanFactory();
        beans.registerSingleton("routerFunctionMapping", functional);
        var guard = new HandlerMappingGuard(beans, new RequestMappingHandlerMapping());
        guard.afterSingletonsInstantiated();
        return guard;
    }
}
