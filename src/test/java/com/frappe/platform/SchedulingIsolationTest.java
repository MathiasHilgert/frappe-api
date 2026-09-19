package com.frappe.platform;

import static com.tngtech.archunit.lang.syntax.ArchRuleDefinition.noClasses;

import com.tngtech.archunit.core.importer.ClassFileImporter;
import com.tngtech.archunit.core.importer.ImportOption;
import org.junit.jupiter.api.Test;

/** db-scheduler is an implementation detail of platform scheduling: nothing else sees its types. */
class SchedulingIsolationTest {

    @Test
    void onlyThePlatformSchedulingAdapterDependsOnDbScheduler() {
        var classes = new ClassFileImporter()
                .withImportOption(ImportOption.Predefined.DO_NOT_INCLUDE_TESTS)
                .importPackages("com.frappe");

        noClasses()
                .that()
                .resideOutsideOfPackage("com.frappe.platform.infrastructure.scheduling..")
                .should()
                .dependOnClassesThat()
                .resideInAPackage("com.github.kagkarlsson..")
                .check(classes);
    }
}
