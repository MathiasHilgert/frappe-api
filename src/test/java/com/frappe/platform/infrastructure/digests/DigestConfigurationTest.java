package com.frappe.platform.infrastructure.digests;

import static org.assertj.core.api.Assertions.assertThat;

import com.frappe.platform.KeyedDigests;
import org.junit.jupiter.api.Test;
import org.springframework.boot.test.context.runner.ApplicationContextRunner;

class DigestConfigurationTest {

    private final ApplicationContextRunner context =
            new ApplicationContextRunner().withUserConfiguration(DigestConfiguration.class);

    @Test
    void providesKeyedDigestsKeyedByTheDigestPepper() {
        context.withPropertyValues("frappe.digests.pepper=" + "not-a-secret-".repeat(3))
                .run(app -> assertThat(app.getBean(KeyedDigests.class).digestOf("identity.email", "ana@example.com"))
                        .isEqualTo(new HmacKeyedDigests("not-a-secret-".repeat(3))
                                .digestOf("identity.email", "ana@example.com")));
    }
}
