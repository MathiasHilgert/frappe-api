package com.frappe;

import org.springframework.boot.SpringApplication;
import org.springframework.boot.autoconfigure.SpringBootApplication;
import org.springframework.modulith.Modulithic;

/**
 * Entry point of the Frappé API modular monolith. {@code platform} is a shared module: every module depends on its
 * kernel and needs its infrastructure (use cases, outbox, telemetry), so module tests bootstrap it with every module.
 */
@SpringBootApplication
@Modulithic(sharedModules = "platform")
public class FrappeApiApplication {

    /** Creates the application configuration; instantiated by Spring. */
    FrappeApiApplication() {}

    /**
     * Starts the application.
     *
     * @param args command-line arguments passed to Spring Boot
     */
    public static void main(String[] args) {
        SpringApplication.run(FrappeApiApplication.class, args);
    }
}
