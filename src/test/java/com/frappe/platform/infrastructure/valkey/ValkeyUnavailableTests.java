package com.frappe.platform.infrastructure.valkey;

import com.frappe.TestNatsConfiguration;
import com.frappe.TestcontainersConfiguration;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.resttestclient.autoconfigure.AutoConfigureRestTestClient;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.context.annotation.Import;
import org.springframework.test.context.ActiveProfiles;
import org.springframework.test.web.servlet.client.RestTestClient;

/**
 * Valkey holds only codes and rate limits: without it the API starts, and everything else keeps serving and stays
 * healthy.
 */
@SpringBootTest(
        webEnvironment = SpringBootTest.WebEnvironment.RANDOM_PORT,
        properties = {
            "spring.data.redis.url=redis://localhost:1",
            "spring.data.redis.timeout=500ms",
            "spring.data.redis.connect-timeout=500ms"
        })
@AutoConfigureRestTestClient
@Import({TestcontainersConfiguration.class, TestNatsConfiguration.class})
@ActiveProfiles("local")
class ValkeyUnavailableTests {

    @Autowired
    RestTestClient http;

    @Test
    void startsAndStaysHealthyWithoutValkey() {
        // When / Then
        http.get().uri("/actuator/health").exchange().expectStatus().isOk();
    }
}
