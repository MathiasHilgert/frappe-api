package com.frappe;

import static com.tngtech.archunit.lang.syntax.ArchRuleDefinition.fields;
import static com.tngtech.archunit.lang.syntax.ArchRuleDefinition.noClasses;
import static com.tngtech.archunit.library.GeneralCodingRules.NO_CLASSES_SHOULD_USE_FIELD_INJECTION;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

import com.tngtech.archunit.core.domain.JavaClasses;
import com.tngtech.archunit.core.importer.ClassFileImporter;
import com.tngtech.archunit.core.importer.ImportOption;
import com.tngtech.archunit.lang.ArchRule;
import fixtures.coding.CodingRuleViolations;
import java.time.Clock;
import java.time.Instant;
import java.time.ZoneId;
import java.util.UUID;
import org.junit.jupiter.api.Test;

/**
 * Coding rules for production code: constructor injection only, no static mutable state, and time and ids only from
 * the injected {@link Clock} and {@code IdGenerator}. Each rule is proven against fixtures that break it.
 */
class CodingRulesTests {

    // The one place allowed to read the system clock: the configuration that provides the application Clock bean.
    private static final String CLOCK_CONFIGURATION = "com.frappe.platform.infrastructure.ids.IdConfiguration";

    private static final ArchRule NO_STATIC_MUTABLE_FIELDS = fields().that()
            .areStatic()
            .should()
            .beFinal()
            .because("static mutable state is shared across requests and tests; inject state instead")
            .allowEmptyShould(true);

    private static final ArchRule TIME_AND_IDS_COME_FROM_INJECTED_BEANS = noClasses()
            .that()
            .doNotHaveFullyQualifiedName(CLOCK_CONFIGURATION)
            .should()
            .callMethod(Instant.class, "now")
            .orShould()
            .callMethod(UUID.class, "randomUUID")
            .orShould()
            .callMethod(Clock.class, "systemUTC")
            .orShould()
            .callMethod(Clock.class, "systemDefaultZone")
            .orShould()
            .callMethod(Clock.class, "system", ZoneId.class)
            .because("time comes from the injected Clock and ids from the injected IdGenerator, so tests control both")
            .allowEmptyShould(true);

    private static final JavaClasses PRODUCTION = new ClassFileImporter()
            .withImportOption(ImportOption.Predefined.DO_NOT_INCLUDE_TESTS)
            .importPackages("com.frappe");

    private static final JavaClasses FIXTURES = new ClassFileImporter().importPackages("fixtures.coding");

    @Test
    void productionCodeFollowsEveryCodingRule() {
        NO_CLASSES_SHOULD_USE_FIELD_INJECTION.check(PRODUCTION);
        NO_STATIC_MUTABLE_FIELDS.check(PRODUCTION);
        TIME_AND_IDS_COME_FROM_INJECTED_BEANS.check(PRODUCTION);
    }

    @Test
    void fieldInjectionIsRejected() {
        assertThatThrownBy(() -> NO_CLASSES_SHOULD_USE_FIELD_INJECTION.check(FIXTURES))
                .hasMessageContaining(CodingRuleViolations.FieldInjected.class.getName())
                .hasMessageNotContaining(CodingRuleViolations.Compliant.class.getName());
    }

    @Test
    void staticMutableFieldsAreRejected() {
        assertThatThrownBy(() -> NO_STATIC_MUTABLE_FIELDS.check(FIXTURES))
                .hasMessageContaining(CodingRuleViolations.StaticMutableState.class.getName() + ".counter")
                .hasMessageNotContaining(CodingRuleViolations.Compliant.class.getName());
    }

    @Test
    void readingTheWallClockOrRandomIdsDirectlyIsRejected() {
        assertThatThrownBy(() -> TIME_AND_IDS_COME_FROM_INJECTED_BEANS.check(FIXTURES))
                .hasMessageContaining(CodingRuleViolations.ReadsTheWallClock.class.getName())
                .hasMessageContaining(CodingRuleViolations.CreatesRandomIds.class.getName())
                .hasMessageContaining(CodingRuleViolations.CreatesItsOwnClock.class.getName())
                .hasMessageNotContaining(CodingRuleViolations.Compliant.class.getName());
    }
}
