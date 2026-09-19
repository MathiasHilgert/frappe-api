package com.frappe.platform.infrastructure.web;

import static org.assertj.core.api.Assertions.assertThat;

import com.frappe.TestNatsConfiguration;
import com.frappe.TestcontainersConfiguration;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.boot.webmvc.test.autoconfigure.AutoConfigureMockMvc;
import org.springframework.context.annotation.Import;
import org.springframework.http.HttpStatus;
import org.springframework.test.web.servlet.assertj.MockMvcTester;

/**
 * Outside the local profile the spec stays available and the Scalar API reference is not served. The database credentials stand in
 * for the environment variables a deployment sets; the URL comes from the test container.
 */
@SpringBootTest(
        properties = {
            "FRAPPE_DB_URL=jdbc:postgresql://replaced-by-the-test-container/frappe",
            "FRAPPE_APP_PASSWORD=frappe_app",
            "FRAPPE_OWNER_PASSWORD=frappe_owner"
        })
@AutoConfigureMockMvc
@Import({TestcontainersConfiguration.class, TestNatsConfiguration.class, OpenApiTests.Routes.class})
class OpenApiOutsideLocalProfileTests {

    @Autowired
    MockMvcTester http;

    @Test
    void theScalarApiReferenceIsNotServed() {
        assertThat(http.get().uri("/scalar")).hasStatus(HttpStatus.NOT_FOUND);
    }

    @Test
    void theSpecRequiresABearerTokenOnEveryNonPublicRoute() {
        OpenApiTests.assertSpecDocumentsPostures(http);
    }
}
