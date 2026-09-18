package com.frappe.platform.infrastructure.ids;

import com.frappe.platform.IdGenerator;
import java.time.Clock;
import org.springframework.boot.autoconfigure.condition.ConditionalOnMissingBean;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;

/** Provides the application clock and id generator; tests replace either with fixed ones. */
@Configuration(proxyBeanMethods = false)
class IdConfiguration {

    /**
     * The UTC system clock, the only time source of the application.
     *
     * @return the system clock in UTC
     */
    @Bean
    @ConditionalOnMissingBean
    Clock clock() {
        return Clock.systemUTC();
    }

    /**
     * UUIDv7 generator reading the application clock.
     *
     * @param clock the application clock
     * @return the id generator
     */
    @Bean
    @ConditionalOnMissingBean
    IdGenerator idGenerator(Clock clock) {
        return new UuidV7IdGenerator(clock);
    }
}
