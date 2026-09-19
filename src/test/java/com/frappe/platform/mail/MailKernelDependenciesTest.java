package com.frappe.platform.mail;

import static com.tngtech.archunit.lang.syntax.ArchRuleDefinition.classes;

import com.tngtech.archunit.core.importer.ClassFileImporter;
import com.tngtech.archunit.core.importer.ImportOption;
import org.junit.jupiter.api.Test;

/**
 * The mail kernel is what modules compile against, so it exposes plain Java only: JTE, Resend, Spring Mail and
 * Micrometer stay in {@code platform.infrastructure.mail}.
 */
class MailKernelDependenciesTest {

    private static final String KERNEL = "com.frappe.platform.mail";

    @Test
    void theKernelDependsOnlyOnJavaAndItself() {
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
                .resideInAnyPackage("java..", KERNEL)
                .allowEmptyShould(false)
                .check(kernel);
    }
}
