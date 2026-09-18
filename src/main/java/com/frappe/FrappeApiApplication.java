package com.frappe;

import org.springframework.boot.SpringApplication;
import org.springframework.boot.autoconfigure.SpringBootApplication;

/** Entry point of the Frappé API modular monolith. */
@SpringBootApplication
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
