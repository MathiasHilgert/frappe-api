package com.frappe.platform.infrastructure.usecase;

import static org.assertj.core.api.Assertions.assertThat;

import fixtures.registration.application.RegisteredUseCases;
import io.micrometer.observation.ObservationRegistry;
import org.junit.jupiter.api.Test;
import org.springframework.boot.autoconfigure.AutoConfigurationPackage;
import org.springframework.boot.test.context.runner.ApplicationContextRunner;
import org.springframework.context.annotation.Configuration;

/** The stereotype alone makes a use case a bean: no Spring annotation on the class is needed. */
class UseCaseRegistrationTest {

    /** Stands in for the application class: Boot's auto-configuration package is where use cases are found. */
    @Configuration(proxyBeanMethods = false)
    @AutoConfigurationPackage(basePackageClasses = RegisteredUseCases.class)
    static class Application {}

    @Test
    void useCasesInTheApplicationPackagesAreBeans() {
        new ApplicationContextRunner()
                .withUserConfiguration(Application.class, UseCaseConfiguration.class)
                .withBean(RecordingTransactionManager.class)
                .withBean(ObservationRegistry.class, ObservationRegistry::create)
                .run(context -> {
                    assertThat(context).hasSingleBean(RegisteredUseCases.OpenTab.class);
                    assertThat(context).hasSingleBean(RegisteredUseCases.FindTab.class);
                    assertThat(context).hasSingleBean(RegisteredUseCases.SplitTab.class);
                    assertThat(context).doesNotHaveBean(RegisteredUseCases.NotAUseCase.class);
                });
    }
}
