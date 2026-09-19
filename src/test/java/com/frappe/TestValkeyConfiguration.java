package com.frappe;

import com.redis.testcontainers.RedisContainer;
import org.springframework.boot.test.context.TestConfiguration;
import org.springframework.boot.testcontainers.service.connection.ServiceConnection;
import org.springframework.context.annotation.Bean;
import org.testcontainers.utility.DockerImageName;

/**
 * A fresh Valkey 9 per Spring test context, matching the compose image. Spring Boot does not recognise the valkey image
 * by name, so the service connection is named {@code redis} (Valkey speaks the same protocol). Not reused: tests may
 * stop it, and it starts in well under a second.
 */
@TestConfiguration(proxyBeanMethods = false)
public class TestValkeyConfiguration {

    public static final DockerImageName VALKEY_IMAGE = DockerImageName.parse("valkey/valkey:9-alpine");

    @Bean
    @ServiceConnection(name = "redis")
    RedisContainer valkeyContainer() {
        return new RedisContainer(VALKEY_IMAGE);
    }
}
