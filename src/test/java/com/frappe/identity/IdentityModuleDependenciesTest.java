package com.frappe.identity;

import static org.assertj.core.api.Assertions.assertThat;

import com.frappe.FrappeApiApplication;
import com.tngtech.archunit.core.importer.ImportOption;
import org.junit.jupiter.api.Test;
import org.springframework.modulith.core.ApplicationModules;

/** identity depends only on platform: a dependency on any other module fails {@code ModularityTests}. */
class IdentityModuleDependenciesTest {

    // Production code, the probe module and one identity fixture that depends on probe.
    private static final ImportOption WITH_A_PROBE_DEPENDENCY =
            location -> new ImportOption.DoNotIncludeTests().includes(location)
                    || location.contains("/com/frappe/probe/")
                    || location.contains("/com/frappe/identity/ProbeDependentFixture");

    @Test
    void aDependencyOfIdentityOnAnotherModuleIsAViolation() {
        // Given
        var modules = ApplicationModules.of(FrappeApiApplication.class, WITH_A_PROBE_DEPENDENCY);
        var identity = modules.getModuleByName("identity").orElseThrow();

        // When
        var violations = identity.detectDependencies(modules);

        // Then
        assertThat(violations.hasViolations()).isTrue();
        assertThat(violations.getMessage()).contains(ProbeDependentFixture.class.getName());
    }

    @Test
    void identityDependsOnThePlatformOnly() {
        // Given
        var modules = ApplicationModules.of(FrappeApiApplication.class);
        var identity = modules.getModuleByName("identity").orElseThrow();

        // When
        var violations = identity.detectDependencies(modules);

        // Then
        assertThat(violations.hasViolations()).isFalse();
        assertThat(identity.getAllowedDependencies(modules).isEmpty()).isFalse();
    }
}
