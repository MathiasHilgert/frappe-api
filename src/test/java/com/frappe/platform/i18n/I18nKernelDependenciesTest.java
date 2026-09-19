package com.frappe.platform.i18n;

import static com.tngtech.archunit.lang.syntax.ArchRuleDefinition.classes;

import com.tngtech.archunit.core.domain.JavaClass;
import com.tngtech.archunit.core.importer.ClassFileImporter;
import com.tngtech.archunit.core.importer.ImportOption;
import org.junit.jupiter.api.Test;

/**
 * The localization kernel is what modules compile against, so it exposes plain Java only: Spring, ICU4J and the
 * servlet API stay in {@code platform.infrastructure.i18n}.
 */
class I18nKernelDependenciesTest {

    private static final String KERNEL = "com.frappe.platform.i18n";

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
                .check(kernel);
    }

    static boolean isPackageInfo(JavaClass type) {
        return type.getSimpleName().equals("package-info");
    }
}
