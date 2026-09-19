package com.frappe.platform.infrastructure.digests;

import com.frappe.platform.KeyedDigests;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;

/** Provides {@link KeyedDigests}, keyed with the digest pepper. */
@Configuration(proxyBeanMethods = false)
class DigestConfiguration {

    /** Creates the configuration; instantiated by Spring. */
    DigestConfiguration() {}

    /**
     * HMAC-SHA256 digests of personal values.
     *
     * @param pepper the digest key ({@code FRAPPE_DIGEST_PEPPER}); not {@code FRAPPE_SECRET_PEPPER}
     * @return the digests
     */
    @Bean
    KeyedDigests keyedDigests(@Value("${frappe.digests.pepper}") String pepper) {
        return new HmacKeyedDigests(pepper);
    }
}
