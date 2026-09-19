package com.frappe.platform.infrastructure.valkey;

import com.frappe.platform.IdGenerator;
import com.frappe.platform.RateLimiter;
import com.frappe.platform.ShortLivedSecretStore;
import java.time.Clock;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.data.redis.connection.lettuce.LettuceConnectionFactory;
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
     * @param clock the application clock
     * @param ids the application id generator
     * @return the secret store
     */
    @Bean
    ShortLivedSecretStore shortLivedSecretStore(StringRedisTemplate redis, Clock clock, IdGenerator ids) {
        return new ValkeyShortLivedSecretStore(
                redis, Argon2PasswordEncoder.defaultsForSpringSecurity_v5_8(), clock, ids);
    }

    /**
     * The rate limiter, on the shared Lettuce connection.
     *
     * @param connections Spring Boot's Lettuce connection factory
     * @param clock the application clock
     * @return the rate limiter
     */
    @Bean
    RateLimiter rateLimiter(LettuceConnectionFactory connections, Clock clock) {
        return new ValkeyRateLimiter(connections, clock);
    }
}
