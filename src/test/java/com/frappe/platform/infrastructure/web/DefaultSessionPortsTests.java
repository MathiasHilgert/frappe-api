package com.frappe.platform.infrastructure.web;

import static org.assertj.core.api.Assertions.assertThat;
import static org.springframework.security.test.web.servlet.setup.SecurityMockMvcConfigurers.springSecurity;

import com.frappe.platform.TenantScope;
import com.frappe.platform.web.Access;
import com.frappe.platform.web.Posture;
import com.frappe.platform.web.ResolvedSession;
import com.frappe.platform.web.SessionKind;
import com.frappe.platform.web.SessionResolver;
import java.util.Optional;
import java.util.UUID;
import java.util.function.Supplier;
import org.junit.jupiter.api.Test;
import org.springframework.boot.autoconfigure.AutoConfigurations;
import org.springframework.boot.http.converter.autoconfigure.HttpMessageConvertersAutoConfiguration;
import org.springframework.boot.security.autoconfigure.SecurityAutoConfiguration;
import org.springframework.boot.security.autoconfigure.web.servlet.ServletWebSecurityAutoConfiguration;
import org.springframework.boot.test.context.runner.WebApplicationContextRunner;
import org.springframework.boot.webmvc.autoconfigure.DispatcherServletAutoConfiguration;
import org.springframework.boot.webmvc.autoconfigure.WebMvcAutoConfiguration;
import org.springframework.http.HttpHeaders;
import org.springframework.http.HttpStatus;
import org.springframework.test.web.servlet.assertj.MockMvcTester;
import org.springframework.test.web.servlet.setup.MockMvcBuilders;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.RestController;
import org.springframework.web.context.WebApplicationContext;

/**
 * Until identity implements {@code SessionResolver}, no bearer token resolves and non-public routes answer 401; until
 * access implements {@code BusinessMembership}, no person enters a business.
 */
class DefaultSessionPortsTests {

    private final WebApplicationContextRunner runner = new WebApplicationContextRunner()
            .withConfiguration(AutoConfigurations.of(
                    WebMvcAutoConfiguration.class,
                    DispatcherServletAutoConfiguration.class,
                    HttpMessageConvertersAutoConfiguration.class,
                    SecurityAutoConfiguration.class,
                    ServletWebSecurityAutoConfiguration.class))
            .withPropertyValues("spring.web.resources.add-mappings=false")
            .withBean(TenantScope.class, DirectTenantScope::new)
            .withUserConfiguration(RouteConfiguration.class, SecurityConfiguration.class);

    /** Runs work unchanged; this slice has no database to bind a tenant for. */
    static final class DirectTenantScope implements TenantScope {

        @Override
        public <T> T callAs(UUID tenantId, Supplier<T> work) {
            return work.get();
        }

        @Override
        public void runAs(UUID tenantId, Runnable work) {
            work.run();
        }

        @Override
        public Optional<UUID> current() {
            return Optional.empty();
        }
    }

    @RestController
    @Access(Posture.AUTHENTICATED)
    static class BusinessRoute {

        @GetMapping("/businesses/{businessId}/test/default-business")
        String answer() {
            return "business";
        }
    }

    @RestController
    @Access(Posture.AUTHENTICATED)
    static class AuthenticatedRoute {

        @GetMapping("/test/default-self")
        String answer() {
            return "self";
        }
    }

    @RestController
    @Access(Posture.PUBLIC)
    static class PublicRoute {

        @GetMapping("/test/default-public")
        String answer() {
            return "public";
        }
    }

    @Test
    void withoutASessionResolverEveryBearerTokenIsRefusedOnANonPublicRoute() {
        runner.withBean(AuthenticatedRoute.class).run(context -> {
            // When
            var result = mvc(context)
                    .get()
                    .uri("/v1/test/default-self")
                    .header(HttpHeaders.AUTHORIZATION, "Bearer any-token");

            // Then
            assertThat(result).hasStatus(HttpStatus.UNAUTHORIZED);
        });
    }

    @Test
    void withoutASessionResolverPublicRoutesStillAnswer() {
        runner.withBean(PublicRoute.class).run(context -> {
            // When
            var result = mvc(context)
                    .get()
                    .uri("/v1/test/default-public")
                    .header(HttpHeaders.AUTHORIZATION, "Bearer any-token");

            // Then
            assertThat(result).hasStatusOk();
        });
    }

    @Test
    void withoutABusinessMembershipNoPersonEntersABusiness() {
        SessionResolver person =
                token -> Optional.of(new ResolvedSession(UUID.randomUUID(), UUID.randomUUID(), SessionKind.PERSON));
        runner.withBean(BusinessRoute.class)
                .withBean(SessionResolver.class, () -> person)
                .run(context -> {
                    // When
                    var result = mvc(context)
                            .get()
                            .uri("/v1/businesses/" + UUID.randomUUID() + "/test/default-business")
                            .header(HttpHeaders.AUTHORIZATION, "Bearer person-token");

                    // Then
                    assertThat(result).hasStatus(HttpStatus.NOT_FOUND);
                });
    }

    private static MockMvcTester mvc(WebApplicationContext context) {
        return MockMvcTester.create(MockMvcBuilders.webAppContextSetup(context)
                .apply(springSecurity())
                .build());
    }
}
