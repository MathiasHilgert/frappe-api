package com.frappe.platform.web;

import static com.tngtech.archunit.lang.syntax.ArchRuleDefinition.classes;

import com.tngtech.archunit.core.importer.ClassFileImporter;
import com.tngtech.archunit.core.importer.ImportOption;
import org.junit.jupiter.api.Test;

/**
 * The HTTP contracts modules compile against ({@code @Access}, sessions, problems) are plain Java: Spring MVC, Spring
 * Security and the servlet API stay in {@code platform.infrastructure.web}.
 */
class WebKernelDependenciesTest {

    private static final String KERNEL = "com.frappe.platform.web";

    @Test
    void theWebKernelDependsOnlyOnJavaAndTheKernel() {
        // Given
        var kernel = new ClassFileImporter()
                .withImportOption(ImportOption.Predefined.DO_NOT_INCLUDE_TESTS)
                .importPackages(KERNEL);

        // When / Then: package-info is excluded, its @NamedInterface is Spring Modulith metadata, not a code dependency
        classes()
                .that()
                .resideInAPackage(KERNEL)
                .and()
                .doNotHaveSimpleName("package-info")
                .should()
                .onlyDependOnClassesThat()
                .resideInAnyPackage("java..", "com.frappe.platform", KERNEL)
                .check(kernel);
    }
}
