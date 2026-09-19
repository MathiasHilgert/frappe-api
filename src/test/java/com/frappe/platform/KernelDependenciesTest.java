package com.frappe.platform;

import static com.tngtech.archunit.lang.syntax.ArchRuleDefinition.classes;

import com.tngtech.archunit.core.importer.ClassFileImporter;
import com.tngtech.archunit.core.importer.ImportOption;
import org.junit.jupiter.api.Test;

/**
 * The platform kernel (the root package every module depends on) exposes plain Java only: ports, keys and purposes
 * never leak Spring, Lettuce, Bucket4j or crypto types, so modules stay decoupled from how the platform implements
 * them.
 */
class KernelDependenciesTest {

    @Test
    void theKernelDependsOnTheJdkOnly() {
        var kernel = new ClassFileImporter()
                .withImportOption(ImportOption.Predefined.DO_NOT_INCLUDE_TESTS)
                .importPackages("com.frappe.platform");

        classes()
                .that()
                .resideInAPackage("com.frappe.platform")
                .should()
                .onlyDependOnClassesThat()
                .resideInAnyPackage("java..", "com.frappe.platform")
                .check(kernel);
    }
}
