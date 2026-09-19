package com.frappe.platform.infrastructure.valkey;

import com.frappe.platform.ShortLivedSecretStore;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.data.redis.core.StringRedisTemplate;
import org.springframework.security.crypto.argon2.Argon2PasswordEncoder;

/** Wires the Valkey-backed platform ports onto the Lettuce client Spring Boot configures. */
@Configuration(proxyBeanMethods = false)
class ValkeyConfiguration {

    /** Creates the configuration; instantiated by Spring. */
    ValkeyConfiguration() {}

    /**
     * The secret store, hashing with Spring Security's current Argon2id defaults.
     *
     * @param redis the Valkey client
     * @return the secret store
     */
    @Bean
    ShortLivedSecretStore shortLivedSecretStore(StringRedisTemplate redis) {
        return new ValkeyShortLivedSecretStore(redis, Argon2PasswordEncoder.defaultsForSpringSecurity_v5_8());
    }
}
