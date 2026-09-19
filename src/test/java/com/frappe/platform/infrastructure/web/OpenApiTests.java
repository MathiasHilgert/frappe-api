package com.frappe.platform.infrastructure.web;

import static org.assertj.core.api.Assertions.assertThat;

import com.frappe.TestNatsConfiguration;
import com.frappe.TestcontainersConfiguration;
import com.frappe.platform.web.Access;
import com.frappe.platform.web.Posture;
import java.util.List;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.boot.test.context.TestConfiguration;
import org.springframework.boot.webmvc.test.autoconfigure.AutoConfigureMockMvc;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Import;
import org.springframework.http.HttpStatus;
import org.springframework.test.context.ActiveProfiles;
import org.springframework.test.web.servlet.assertj.MockMvcTester;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.RestController;

/**
 * The generated OpenAPI spec documents each route's posture; the Scalar API reference is served in the local profile.
 */
@SpringBootTest
@AutoConfigureMockMvc
@Import({TestcontainersConfiguration.class, TestNatsConfiguration.class, OpenApiTests.Routes.class})
@ActiveProfiles("local")
class OpenApiTests {

    static final String SPEC = "/v3/api-docs";

    @TestConfiguration(proxyBeanMethods = false)
    static class Routes {

        @Bean
        PublicRoute publicDocumentedRoute() {
            return new PublicRoute();
        }

        @Bean
        AuthenticatedRoute authenticatedDocumentedRoute() {
            return new AuthenticatedRoute();
        }
    }

    @RestController
    @Access(Posture.PUBLIC)
    static class PublicRoute {

        @GetMapping("/test/docs/public")
        String answer() {
            return "public";
        }
    }

    @RestController
    @Access(Posture.AUTHENTICATED)
    static class AuthenticatedRoute {

        @GetMapping("/test/docs/self")
        String answer() {
            return "self";
        }
    }

    @Autowired
    MockMvcTester http;

    @Test
    void theScalarApiReferenceIsServedInTheLocalProfile() {
        assertThat(http.get().uri("/scalar")).hasStatusOk();
    }

    @Test
    void swaggerUiIsNotServed() {
        assertThat(http.get().uri("/swagger-ui/index.html")).hasStatus(HttpStatus.NOT_FOUND);
    }

    @Test
    void theSpecRequiresABearerTokenOnEveryNonPublicRoute() {
        assertSpecDocumentsPostures(http);
    }

    /** Shared with the non-local profile test: the spec is the same in every profile. */
    static void assertSpecDocumentsPostures(MockMvcTester http) {
        assertThat(http.get().uri(SPEC + ".yaml")).hasStatusOk();
        var spec = assertThat(http.get().uri(SPEC)).hasStatusOk().bodyJson();
        spec.extractingPath("$.components.securitySchemes.bearer.type").isEqualTo("http");
        spec.extractingPath("$.components.securitySchemes.bearer.scheme").isEqualTo("bearer");
        spec.extractingPath("$.paths['/v1/test/docs/self'].get.security[0].bearer")
                .isEqualTo(List.of());
        spec.extractingPath("$.paths['/v1/test/docs/self'].get.responses['401']")
                .isNotNull();
        spec.doesNotHavePath("$.paths['/v1/test/docs/self'].get.responses['403']");
        spec.doesNotHavePath("$.paths['/v1/test/docs/public'].get.security");
        spec.doesNotHavePath("$.paths['/v1/test/docs/public'].get.responses['401']");
    }
}
