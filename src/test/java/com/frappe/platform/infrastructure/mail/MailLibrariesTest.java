package com.frappe.platform.infrastructure.mail;

import static com.tngtech.archunit.lang.syntax.ArchRuleDefinition.noClasses;

import com.tngtech.archunit.core.importer.ClassFileImporter;
import com.tngtech.archunit.core.importer.ImportOption;
import org.junit.jupiter.api.Test;

/**
 * The mail libraries (Resend, JTE, jsoup, Jakarta and Angus Mail) stay inside this adapter package and JTE's generated
 * templates; modules and the rest of the platform send mail through the {@code Mailer} port only.
 */
class MailLibrariesTest {

    @Test
    void mailLibrariesAreUsedOnlyByTheMailAdapter() {
        // Given
        var production = new ClassFileImporter()
                .withImportOption(ImportOption.Predefined.DO_NOT_INCLUDE_TESTS)
                .importPackages("com.frappe", "gg.jte.generated");

        // When / Then
        noClasses()
                .that()
                .resideOutsideOfPackages("com.frappe.platform.infrastructure.mail..", "gg.jte.generated..")
                .should()
                .dependOnClassesThat()
                .resideInAnyPackage("com.resend..", "gg.jte..", "org.jsoup..", "jakarta.mail..", "org.eclipse.angus..")
                .check(production);
    }
}
