package com.frappe.platform.infrastructure.web;

import static org.assertj.core.api.Assertions.assertThat;

import com.frappe.platform.web.Access;
import com.frappe.platform.web.Posture;
import org.junit.jupiter.api.Test;
import org.springframework.boot.autoconfigure.AutoConfigurations;
import org.springframework.boot.http.converter.autoconfigure.HttpMessageConvertersAutoConfiguration;
import org.springframework.boot.test.context.runner.WebApplicationContextRunner;
import org.springframework.boot.webmvc.autoconfigure.DispatcherServletAutoConfiguration;
import org.springframework.boot.webmvc.autoconfigure.WebMvcAutoConfiguration;
import org.springframework.boot.webmvc.autoconfigure.error.ErrorMvcAutoConfiguration;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RestController;
import org.springframework.web.servlet.mvc.method.annotation.RequestMappingHandlerMapping;

/** Startup checks over the routes: every route declares its posture and is one class with one mapped method. */
class RouteStartupTests {

    private final WebApplicationContextRunner runner = new WebApplicationContextRunner()
            .withConfiguration(AutoConfigurations.of(
                    WebMvcAutoConfiguration.class,
                    DispatcherServletAutoConfiguration.class,
                    HttpMessageConvertersAutoConfiguration.class,
                    ErrorMvcAutoConfiguration.class))
            .withUserConfiguration(RouteConfiguration.class);

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
    @Access(Posture.AUTHENTICATED)
    static class ValidRoute {

        @GetMapping("/valid")
        String answer() {
            return "answered";
        }
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
