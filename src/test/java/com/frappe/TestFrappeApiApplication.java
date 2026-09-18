package com.frappe;

import org.springframework.boot.SpringApplication;

public class TestFrappeApiApplication {

    public static void main(String[] args) {
        SpringApplication.from(FrappeApiApplication::main)
                .with(TestcontainersConfiguration.class)
                .run(args);
    }
}
