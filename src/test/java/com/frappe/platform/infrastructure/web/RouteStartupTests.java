package com.frappe.platform.infrastructure.web;

import static org.assertj.core.api.Assertions.assertThat;

import com.frappe.platform.infrastructure.misplaced.MisplacedRoutes;
import com.frappe.platform.web.Access;
import com.frappe.platform.web.Posture;
import com.frappe.platform.web.ResolvedSession;
import jakarta.servlet.http.HttpServletRequest;
import java.util.Map;
import org.junit.jupiter.api.Test;
import org.springframework.boot.autoconfigure.AutoConfigurations;
import org.springframework.boot.http.converter.autoconfigure.HttpMessageConvertersAutoConfiguration;
import org.springframework.boot.test.context.runner.WebApplicationContextRunner;
import org.springframework.boot.webmvc.actuate.endpoint.web.ImpostorEndpointHandlerMapping;
import org.springframework.boot.webmvc.autoconfigure.DispatcherServletAutoConfiguration;
import org.springframework.boot.webmvc.autoconfigure.WebMvcAutoConfiguration;
import org.springframework.boot.webmvc.autoconfigure.error.ErrorMvcAutoConfiguration;
import org.springframework.web.HttpRequestHandler;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RestController;
import org.springframework.web.servlet.function.RouterFunction;
import org.springframework.web.servlet.function.RouterFunctions;
import org.springframework.web.servlet.function.ServerResponse;
import org.springframework.web.servlet.function.support.RouterFunctionMapping;
import org.springframework.web.servlet.handler.AbstractHandlerMapping;
import org.springframework.web.servlet.handler.SimpleUrlHandlerMapping;
import org.springframework.web.servlet.mvc.method.annotation.RequestMappingHandlerMapping;

/** Startup checks over the routes: every route declares its posture and is one class with one mapped method. */
class RouteStartupTests {

    private final WebApplicationContextRunner runner = new WebApplicationContextRunner()
            .withConfiguration(AutoConfigurations.of(
                    WebMvcAutoConfiguration.class,
                    DispatcherServletAutoConfiguration.class,
                    HttpMessageConvertersAutoConfiguration.class,
                    ErrorMvcAutoConfiguration.class))
            .withPropertyValues("spring.web.resources.add-mappings=false")
            .withUserConfiguration(RouteConfiguration.class);

    static class CustomHandlerMapping extends AbstractHandlerMapping {

        @Override
        protected Object getHandlerInternal(HttpServletRequest request) {
            return null;
        }
    }

    @RestController
    static class RouteWithoutAccess {

        @GetMapping("/without-access")
        String answer() {
            return "answered";
        }
    }

    @RestController
    @Access(Posture.PUBLIC)
    static class ControllerWithTwoMethods {

        @GetMapping("/two-methods")
        String read() {
            return "read";
        }

        @PostMapping("/two-methods")
        String write() {
            return "written";
        }
    }

    @RestController
    @Access(Posture.PUBLIC)
    static class PublicRouteAskingForTheCaller {

        @GetMapping("/public-caller")
        String answer(ResolvedSession caller) {
            return caller.principalId().toString();
        }
    }

    @RestController
    @Access(Posture.AUTHENTICATED)
    static class ValidRoute {

        @GetMapping("/valid")
        String answer() {
            return "answered";
        }
    }

    @RestController
    @Access(Posture.AUTHENTICATED)
    static class CatchAllBusinessRoute {

        @GetMapping("/businesses/{*rest}")
        String answer() {
            return "answered";
        }
    }

    @RestController
    @Access(Posture.AUTHENTICATED)
    static class ConstrainedBusinessRoute {

        @GetMapping("/businesses/{businessId:[a-z0-9-]+}/tabs")
        String answer() {
            return "answered";
        }
    }

    @RestController
    @Access(Posture.PUBLIC)
    static class LiteralBusinessRoute {

        @GetMapping("/businesses/featured")
        String answer() {
            return "answered";
        }
    }

    @Test
    void startupFailsForACatchAllUnderTheBusinessPath() {
        runner.withBean(CatchAllBusinessRoute.class)
                .run(context -> assertThat(context)
                        .hasFailed()
                        .getFailure()
                        .rootCause()
                        .isInstanceOf(InvalidRouteException.class)
                        .hasMessageContaining(CatchAllBusinessRoute.class.getName())
                        .hasMessageContaining("/v1/businesses/{businessId}"));
    }

    @Test
    void startupFailsForAnyOtherShapeUnderTheBusinessPath() {
        runner.withBean(ConstrainedBusinessRoute.class)
                .withBean(LiteralBusinessRoute.class)
                .run(context -> assertThat(context)
                        .hasFailed()
                        .getFailure()
                        .rootCause()
                        .isInstanceOf(InvalidRouteException.class)
                        .hasMessageContaining(ConstrainedBusinessRoute.class.getName())
                        .hasMessageContaining(LiteralBusinessRoute.class.getName()));
    }

    @Test
    void startupFailsForARouteWithoutAccessAndNamesTheClass() {
        runner.withBean(RouteWithoutAccess.class)
                .run(context -> assertThat(context)
                        .hasFailed()
                        .getFailure()
                        .rootCause()
                        .isInstanceOf(InvalidRouteException.class)
                        .hasMessageContaining(RouteWithoutAccess.class.getName())
                        .hasMessageContaining("@Access"));
    }

    @Test
    void startupFailsForAControllerWithTwoMappedMethodsAndNamesTheClass() {
        runner.withBean(ControllerWithTwoMethods.class)
                .run(context -> assertThat(context)
                        .hasFailed()
                        .getFailure()
                        .rootCause()
                        .isInstanceOf(InvalidRouteException.class)
                        .hasMessageContaining(ControllerWithTwoMethods.class.getName())
                        .hasMessageContaining("2 mapped methods"));
    }

    @Test
    void startupFailsForARouteOutsideAnInfrastructureWebPackageAndNamesTheClass() {
        runner.withBean(MisplacedRoutes.MisplacedRoute.class)
                .run(context -> assertThat(context)
                        .hasFailed()
                        .getFailure()
                        .rootCause()
                        .isInstanceOf(InvalidRouteException.class)
                        .hasMessageContaining(MisplacedRoutes.MisplacedRoute.class.getName())
                        .hasMessageContaining("infrastructure.web"));
    }

    @Test
    void startupFailsForAFunctionalRouteBecauseItCannotDeclareAPosture() {
        runner.withBean(
                        "secretRoutes",
                        RouterFunction.class,
                        () -> RouterFunctions.route()
                                .GET(
                                        "/v1/fn/secret",
                                        request -> ServerResponse.ok().body("secret"))
                                .build())
                .run(context -> assertThat(context)
                        .hasFailed()
                        .getFailure()
                        .isInstanceOf(InvalidRouteException.class)
                        .hasMessageContaining("secretRoutes")
                        .hasMessageContaining("RouterFunction"));
    }

    @Test
    void startupFailsForAHandlerMappingThatServesPathsOutsideTheRoutes() {
        runner.withBean(
                        "sneakyMapping",
                        SimpleUrlHandlerMapping.class,
                        () -> new SimpleUrlHandlerMapping(
                                Map.of("/v1/sneaky", (HttpRequestHandler) (request, response) -> {})))
                .run(context -> assertThat(context)
                        .hasFailed()
                        .getFailure()
                        .isInstanceOf(InvalidRouteException.class)
                        .hasMessageContaining("sneakyMapping")
                        .hasMessageContaining("/v1/sneaky"));
    }

    @Test
    void startupFailsForAFunctionalMappingGivenRoutesInCode() {
        runner.withBean(
                        "shadowMapping",
                        RouterFunctionMapping.class,
                        () -> new RouterFunctionMapping(RouterFunctions.route()
                                .GET("/v1/valid", request -> ServerResponse.ok().body("shadow"))
                                .build()))
                .run(context -> assertThat(context)
                        .hasFailed()
                        .getFailure()
                        .isInstanceOf(InvalidRouteException.class)
                        .hasMessageContaining("shadowMapping")
                        .hasMessageContaining("router function"));
    }

    @Test
    void startupFailsForAMappingOrderedBeforeTheAnnotatedRoutes() {
        runner.withBean("earlyMapping", SimpleUrlHandlerMapping.class, () -> new SimpleUrlHandlerMapping(Map.of(), -1))
                .run(context -> assertThat(context)
                        .hasFailed()
                        .getFailure()
                        .isInstanceOf(InvalidRouteException.class)
                        .hasMessageContaining("earlyMapping")
                        .hasMessageContaining("before the annotated routes"));
    }

    @Test
    void startupFailsForACustomHandlerMapping() {
        runner.withBean("customMapping", CustomHandlerMapping.class, CustomHandlerMapping::new)
                .run(context -> assertThat(context)
                        .hasFailed()
                        .getFailure()
                        .isInstanceOf(InvalidRouteException.class)
                        .hasMessageContaining("customMapping"));
    }

    @Test
    void startupFailsForAMappingThatOnlyBorrowsTheActuatorPackage() {
        runner.withBean("impostorMapping", ImpostorEndpointHandlerMapping.class, ImpostorEndpointHandlerMapping::new)
                .run(context -> assertThat(context)
                        .hasFailed()
                        .getFailure()
                        .isInstanceOf(InvalidRouteException.class)
                        .hasMessageContaining("impostorMapping"));
    }

    @Test
    void startupFailsForAPublicRouteThatAsksForTheCaller() {
        runner.withBean(PublicRouteAskingForTheCaller.class)
                .run(context -> assertThat(context)
                        .hasFailed()
                        .getFailure()
                        .rootCause()
                        .isInstanceOf(InvalidRouteException.class)
                        .hasMessageContaining(PublicRouteAskingForTheCaller.class.getName())
                        .hasMessageContaining("ResolvedSession"));
    }

    @Test
    void listsEveryInvalidRouteAtOnce() {
        runner.withBean(RouteWithoutAccess.class)
                .withBean(ControllerWithTwoMethods.class)
                .run(context -> assertThat(context)
                        .hasFailed()
                        .getFailure()
                        .rootCause()
                        .hasMessageContaining(RouteWithoutAccess.class.getName())
                        .hasMessageContaining(ControllerWithTwoMethods.class.getName()));
    }

    @Test
    void startsWithValidRoutesAndServesThemUnderV1WhileFrameworkControllersKeepTheirPaths() {
        runner.withBean(ValidRoute.class).run(context -> {
            // Then the route is prefixed; Boot's error controller (two methods, no @Access) is neither checked nor
            // prefixed
            assertThat(context).hasNotFailed();
            var patterns = context.getBean(RequestMappingHandlerMapping.class).getHandlerMethods().keySet().stream()
                    .flatMap(mapping -> mapping.getPatternValues().stream())
                    .toList();
            assertThat(patterns).contains("/v1/valid", "/error").doesNotContain("/valid", "/v1/error");
        });
    }
}
