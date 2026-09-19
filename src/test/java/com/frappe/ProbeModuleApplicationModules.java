package com.frappe;

import com.tngtech.archunit.core.importer.ImportOption;
import org.springframework.modulith.core.ApplicationModules;
import org.springframework.modulith.core.ApplicationModulesFactory;

/**
 * Spring Modulith's module model for tests: the production modules plus the test-only {@code probe} module, which
 * exercises platform features (use cases) the way a real module does. Everything else stays as in production (Modulith
 * ignores test classes by default), so observability and verification behave the same. Registered in
 * {@code src/test/resources/META-INF/spring.factories}.
 */
public class ProbeModuleApplicationModules implements ApplicationModulesFactory {

    private static final ImportOption PRODUCTION_AND_PROBE_MODULE = location ->
            new ImportOption.DoNotIncludeTests().includes(location) || location.contains("/com/frappe/probe/");

    @Override
    public ApplicationModules of(Class<?> applicationClass) {
        return ApplicationModules.of(applicationClass, PRODUCTION_AND_PROBE_MODULE);
    }
}
