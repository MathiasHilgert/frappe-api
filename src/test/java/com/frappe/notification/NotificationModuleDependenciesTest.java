package com.frappe.notification;

import static org.assertj.core.api.Assertions.assertThat;

import com.frappe.FrappeApiApplication;
import org.junit.jupiter.api.Test;
import org.springframework.modulith.core.ApplicationModule;
import org.springframework.modulith.core.ApplicationModules;

/** notification depends on identity's root package; identity never depends on notification (the SPI avoids a cycle). */
class NotificationModuleDependenciesTest {

    private final ApplicationModules modules = ApplicationModules.of(FrappeApiApplication.class);

    @Test
    void notificationDependsOnIdentitysRootPackageWithinItsAllowedDependencies() {
        // Given
        var notification = module("notification");
        var identity = module("identity");

        // When
        var violations = notification.detectDependencies(modules);

        // Then
        assertThat(violations.hasViolations()).isFalse();
        assertThat(notification.getDirectDependencies(modules).contains(identity))
                .isTrue();
        assertThat(notification.getAllowedDependencies(modules).stream().map(allowed -> allowed.getTargetModule()))
                .as("notification declares identity among its allowed dependencies")
                .contains(identity);
    }

    @Test
    void identityHasNoDependencyOnNotification() {
        // Given
        var identity = module("identity");

        // When
        var dependencies = identity.getDirectDependencies(modules);

        // Then
        assertThat(dependencies.contains(module("notification"))).isFalse();
        assertThat(identity.detectDependencies(modules).hasViolations()).isFalse();
    }

    private ApplicationModule module(String name) {
        return modules.getModuleByName(name).orElseThrow();
    }
}
