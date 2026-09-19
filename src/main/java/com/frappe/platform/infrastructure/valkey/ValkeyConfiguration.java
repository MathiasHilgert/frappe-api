package com.frappe.platform.infrastructure.valkey;

import com.frappe.platform.IdGenerator;
import com.frappe.platform.RateLimiter;
import com.frappe.platform.ShortLivedSecretStore;
import java.time.Clock;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.data.redis.connection.lettuce.LettuceConnectionFactory;
import org.springframework.data.redis.core.StringRedisTemplate;

/** Wires the Valkey-backed platform ports onto the Lettuce client Spring Boot configures. */
@Configuration(proxyBeanMethods = false)
class ValkeyConfiguration {

    /** Creates the configuration; instantiated by Spring. */
    ValkeyConfiguration() {}

    /**
     * The secret store, hashing with Argon2id over a peppered HMAC.
     *
     * @param redis the Valkey client
     * @param pepper the server-side pepper ({@code FRAPPE_SECRET_PEPPER})
     * @param clock the application clock
     * @param ids the application id generator
     * @return the secret store
     */
    @Bean
    ShortLivedSecretStore shortLivedSecretStore(
            StringRedisTemplate redis, @Value("${frappe.secrets.pepper}") String pepper, Clock clock, IdGenerator ids) {
        return new ValkeyShortLivedSecretStore(redis, new PepperedArgon2PasswordEncoder(pepper), clock, ids);
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
