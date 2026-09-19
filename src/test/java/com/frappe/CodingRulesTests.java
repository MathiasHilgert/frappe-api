package com.frappe;

import static com.tngtech.archunit.lang.syntax.ArchRuleDefinition.fields;
import static com.tngtech.archunit.lang.syntax.ArchRuleDefinition.noClasses;
import static com.tngtech.archunit.library.GeneralCodingRules.NO_CLASSES_SHOULD_USE_FIELD_INJECTION;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

import com.tngtech.archunit.base.DescribedPredicate;
import com.tngtech.archunit.core.domain.JavaClass;
import com.tngtech.archunit.core.domain.JavaClasses;
import com.tngtech.archunit.core.domain.JavaField;
import com.tngtech.archunit.core.domain.JavaMethodCall;
import com.tngtech.archunit.core.domain.JavaModifier;
import com.tngtech.archunit.core.importer.ClassFileImporter;
import com.tngtech.archunit.core.importer.ImportOption;
import com.tngtech.archunit.lang.ArchCondition;
import com.tngtech.archunit.lang.ArchRule;
import com.tngtech.archunit.lang.ConditionEvents;
import com.tngtech.archunit.lang.SimpleConditionEvent;
import fixtures.coding.CodingRuleViolations;
import java.time.Clock;
import java.util.UUID;
import org.junit.jupiter.api.Test;

/**
 * Coding rules for production code: constructor injection only, no static mutable state, and time and ids only from
 * the injected {@link Clock} and {@code IdGenerator}. Each rule is proven against fixtures that break it.
 */
class CodingRulesTests {

    private static final JavaClasses PRODUCTION = new ClassFileImporter()
            .withImportOption(ImportOption.Predefined.DO_NOT_INCLUDE_TESTS)
            .importPackages("com.frappe");

    private static final JavaClasses FIXTURES = new ClassFileImporter().importPackages("fixtures.coding");

    // The one code unit allowed to read the system clock: the bean method providing the application Clock.
    private static final CodingRules PRODUCTION_RULES =
            new CodingRules("com.frappe.platform.infrastructure.ids.IdConfiguration.clock");

    private static final CodingRules FIXTURE_RULES =
            new CodingRules(CodingRuleViolations.ClockConfiguration.class.getName() + ".clock");

    @Test
    void productionCodeFollowsEveryCodingRule() {
        NO_CLASSES_SHOULD_USE_FIELD_INJECTION.check(PRODUCTION);
        PRODUCTION_RULES.noStaticMutableState().check(PRODUCTION);
        PRODUCTION_RULES.timeAndIdsComeFromInjectedBeans().check(PRODUCTION);
    }

    @Test
    void fieldInjectionIsRejected() {
        assertThatThrownBy(() -> NO_CLASSES_SHOULD_USE_FIELD_INJECTION.check(FIXTURES))
                .hasMessageContaining(CodingRuleViolations.FieldInjected.class.getName())
                .hasMessageNotContaining(CodingRuleViolations.Compliant.class.getName());
    }

    @Test
    void staticNonFinalFieldsAreRejected() {
        assertThatThrownBy(() -> FIXTURE_RULES.noStaticMutableState().check(FIXTURES))
                .hasMessageContaining(CodingRuleViolations.StaticMutableState.class.getName() + ".counter")
                .hasMessageNotContaining(CodingRuleViolations.Compliant.class.getName());
    }

    @Test
    void staticFinalFieldsOfMutableTypesAreRejected() {
        var owner = CodingRuleViolations.StaticMutableFinalState.class.getName();
        assertThatThrownBy(() -> FIXTURE_RULES.noStaticMutableState().check(FIXTURES))
                .hasMessageContaining(owner + ".PATHS")
                .hasMessageContaining(owner + ".NAMES")
                .hasMessageContaining(owner + ".COUNTER")
                .hasMessageContaining(owner + ".CACHE")
                .hasMessageContaining(owner + ".NAMES_BY_CLASS")
                .hasMessageNotContaining(CodingRuleViolations.Compliant.class.getName());
    }

    @Test
    void readingTheWallClockOrRandomIdsDirectlyIsRejected() {
        assertThatThrownBy(() -> FIXTURE_RULES.timeAndIdsComeFromInjectedBeans().check(FIXTURES))
                .hasMessageContaining(CodingRuleViolations.ReadsTheWallClock.class.getName())
                .hasMessageContaining(CodingRuleViolations.CreatesRandomIds.class.getName())
                .hasMessageContaining(CodingRuleViolations.CreatesItsOwnClock.class.getName())
                .hasMessageNotContaining(CodingRuleViolations.Compliant.class.getName());
    }

    @Test
    void anyNoArgumentNowOrSystemTimeOutsideTheClockBeanMethodIsRejected() {
        var clockConfiguration = CodingRuleViolations.ClockConfiguration.class.getName();
        assertThatThrownBy(() -> FIXTURE_RULES.timeAndIdsComeFromInjectedBeans().check(FIXTURES))
                .hasMessageContaining(CodingRuleViolations.ReadsTheLocalDate.class.getName())
                .hasMessageContaining(CodingRuleViolations.ReadsSystemMillis.class.getName())
                .hasMessageContaining(clockConfiguration + ".anotherClock()")
                .hasMessageNotContaining(clockConfiguration + ".clock()")
                .hasMessageNotContaining(CodingRuleViolations.Compliant.class.getName());
    }

    /**
     * The coding rules, with the one code unit allowed to read the system clock.
     *
     * @param clockBeanMethod fully qualified {@code Class.method} of the bean method providing the application Clock
     */
    record CodingRules(String clockBeanMethod) {

        // Types whose instances are mutable: a static final field of one is shared mutable state all the same.
        private static final DescribedPredicate<JavaClass> MUTABLE_TYPES = DescribedPredicate.describe(
                "a known mutable type (array, mutable collection, atomic, builder, date, ClassValue, ThreadLocal)",
                type -> type.isArray()
                        || type.isAssignableTo(java.util.Collection.class) && !isUnmodifiableFactoryType(type)
                        || type.isAssignableTo(java.util.Map.class) && !isUnmodifiableFactoryType(type)
                        || type.getPackageName().equals("java.util.concurrent.atomic")
                        || type.isAssignableTo(StringBuilder.class)
                        || type.isAssignableTo(StringBuffer.class)
                        || type.isAssignableTo(java.util.Date.class)
                        || type.isAssignableTo(java.util.Calendar.class)
                        || type.isAssignableTo(ClassValue.class)
                        || type.isAssignableTo(ThreadLocal.class));

        ArchRule noStaticMutableState() {
            // Compiler-generated fields (enum $VALUES, switch maps) are the JDK's business, not ours.
            return fields().that()
                    .areStatic()
                    .and()
                    .doNotHaveModifier(JavaModifier.SYNTHETIC)
                    .should(new ArchCondition<>("be final and not of a known mutable type") {
                        @Override
                        public void check(JavaField field, ConditionEvents events) {
                            var mutableValue = field.getModifiers().contains(JavaModifier.FINAL)
                                    && (MUTABLE_TYPES.test(field.getRawType()) || initializedWithMutableType(field));
                            var reassignable = !field.getModifiers().contains(JavaModifier.FINAL);
                            if (reassignable || mutableValue) {
                                events.add(SimpleConditionEvent.violated(
                                        field, field.getFullName() + " is static mutable state; inject it instead"));
                            }
                        }
                    })
                    .allowEmptyShould(true);
        }

        ArchRule timeAndIdsComeFromInjectedBeans() {
            return noClasses()
                    .should(
                            new ArchCondition<>("read the time or create ids outside the injected Clock and IdGenerator"
                                    + " (any no-argument java.time now(), System.currentTimeMillis(), UUID.randomUUID(),"
                                    + " Clock.system*(); only " + clockBeanMethod + " may create the system clock)") {
                                @Override
                                public void check(JavaClass type, ConditionEvents events) {
                                    for (JavaMethodCall call : type.getMethodCallsFromSelf()) {
                                        if (isForbidden(call)) {
                                            events.add(SimpleConditionEvent.satisfied(call, call.getDescription()));
                                        }
                                    }
                                }
                            })
                    .allowEmptyShould(true);
        }

        private boolean isForbidden(JavaMethodCall call) {
            var owner = call.getTargetOwner();
            var name = call.getName();
            var noArguments = call.getTarget().getRawParameterTypes().isEmpty();
            var inClockBeanMethod = (call.getOrigin().getOwner().getName() + "."
                            + call.getOrigin().getName())
                    .equals(clockBeanMethod);
            if (owner.isEquivalentTo(Clock.class) && name.startsWith("system")) {
                return !inClockBeanMethod;
            }
            return owner.getPackageName().equals("java.time") && name.equals("now") && noArguments
                    || owner.isEquivalentTo(System.class) && name.equals("currentTimeMillis")
                    || owner.isEquivalentTo(UUID.class) && name.equals("randomUUID");
        }

        // A static final field assigned a new mutable instance in the static initializer (e.g. a Map field set to a
        // new HashMap) is mutable state even though its declared type is an interface.
        private static boolean initializedWithMutableType(JavaField field) {
            return field.getOwner().getStaticInitializer().stream()
                    .flatMap(initializer -> initializer.getConstructorCallsFromSelf().stream())
                    .anyMatch(call -> MUTABLE_TYPES.test(call.getTargetOwner())
                            && field.getOwner().getFieldAccessesFromSelf().stream()
                                    .anyMatch(access ->
                                            access.getTarget().getName().equals(field.getName())
                                                    && access.getOrigin().equals(call.getOrigin())));
        }

        private static boolean isUnmodifiableFactoryType(JavaClass type) {
            // List.of, Set.of, Map.of and friends return JDK-internal immutable types; the interfaces themselves
            // are judged by the initializer check.
            return type.isInterface() || type.getName().startsWith("java.util.ImmutableCollections");
        }
    }
}
