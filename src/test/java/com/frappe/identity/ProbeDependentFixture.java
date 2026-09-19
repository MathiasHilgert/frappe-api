package com.frappe.identity;

import com.frappe.probe.ProbeRecorded;

/**
 * A test-only identity type that depends on another module ({@code probe}); {@link IdentityModuleDependenciesTest}
 * imports it to prove the dependency is refused. Never part of the production module model.
 */
final class ProbeDependentFixture {

    private ProbeDependentFixture() {}

    static Class<?> dependency() {
        return ProbeRecorded.class;
    }
}
