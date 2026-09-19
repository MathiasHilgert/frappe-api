package com.frappe;

import static com.tngtech.archunit.lang.syntax.ArchRuleDefinition.classes;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

import com.frappe.platform.CommandUseCase;
import com.frappe.platform.QueryUseCase;
import com.tngtech.archunit.base.DescribedPredicate;
import com.tngtech.archunit.core.domain.Dependency;
import com.tngtech.archunit.core.domain.JavaClass;
import com.tngtech.archunit.core.domain.JavaClasses;
import com.tngtech.archunit.core.domain.JavaModifier;
import com.tngtech.archunit.core.importer.ClassFileImporter;
import com.tngtech.archunit.core.importer.ImportOption;
import com.tngtech.archunit.lang.ArchCondition;
import com.tngtech.archunit.lang.ArchRule;
import com.tngtech.archunit.lang.ConditionEvents;
import com.tngtech.archunit.lang.SimpleConditionEvent;
import fixtures.tabs.application.InvalidUseCases;
import fixtures.tabs.application.ValidUseCases;
import fixtures.tabs.domain.InvalidDomain;
import fixtures.tabs.infrastructure.web.MisplacedUseCase;
import java.lang.reflect.Method;
import java.lang.reflect.Modifier;
import java.util.Arrays;
import java.util.List;
import java.util.Set;
import java.util.function.BiPredicate;
import java.util.stream.Stream;
import org.junit.jupiter.api.Test;
import org.springframework.transaction.TransactionDefinition;
import org.springframework.transaction.annotation.AnnotationTransactionAttributeSource;
import org.springframework.transaction.annotation.Isolation;
import org.springframework.transaction.annotation.Propagation;
import org.springframework.transaction.annotation.Transactional;

/**
 * Use cases follow one shape: a class per operation in the module's {@code application} package, marked (directly or
 * through a composed annotation) with exactly one kernel stereotype, proxyable, whose one operation runs in the
 * transaction of its kind. Domain and use-case code depend on an allow-list: domain on the JDK, the kernel and its own
 * module's domain; use cases additionally on their own module (not its infrastructure), other modules' public root
 * packages and, from Spring, only {@code @Transactional} with {@code Propagation} and {@code Isolation}. The kernel
 * itself is checked by {@code KernelDependenciesTest}. Every rule is proven against fixtures (modules under
 * {@code fixtures}) and checked against production code (modules under {@code com.frappe}).
 */
class UseCaseArchitectureTests {

    private static final String KERNEL = "com.frappe.platform";

    // The whole of Spring a use case may name: its transaction annotation and the enums of its attributes.
    private static final Set<String> TRANSACTION_ANNOTATION_TYPES =
            Set.of(Transactional.class.getName(), Propagation.class.getName(), Isolation.class.getName());

    // NESTED is out: Spring Boot's JPA transaction manager does not allow savepoints by default, so a NESTED use case
    // fails at runtime (NestedTransactionNotSupportedException); REQUIRES_NEW covers "commit on its own".
    private static final Set<Integer> OWN_TRANSACTION_PROPAGATIONS =
            Set.of(TransactionDefinition.PROPAGATION_REQUIRED, TransactionDefinition.PROPAGATION_REQUIRES_NEW);

    // Same matching as the platform's scan at runtime: concrete classes carrying the stereotype directly or as a
    // meta-annotation (composed annotations themselves are not use cases).
    private static final DescribedPredicate<JavaClass> USE_CASES = DescribedPredicate.describe(
            "use cases",
            type -> !type.isAnnotation()
                    && !type.isInterface()
                    && !type.getModifiers().contains(JavaModifier.ABSTRACT)
                    && (type.isMetaAnnotatedWith(CommandUseCase.class)
                            || type.isMetaAnnotatedWith(QueryUseCase.class)));

    private static final JavaClasses PRODUCTION = new ClassFileImporter()
            .withImportOption(ImportOption.Predefined.DO_NOT_INCLUDE_TESTS)
            .importPackages("com.frappe");

    private static final JavaClasses FIXTURES = new ClassFileImporter().importPackages("fixtures");

    private static final UseCaseRules PRODUCTION_RULES = new UseCaseRules("com.frappe");

    private static final UseCaseRules FIXTURE_RULES = new UseCaseRules("fixtures");

    @Test
    void productionCodeFollowsEveryRule() {
        PRODUCTION_RULES.all().forEach(rule -> rule.check(PRODUCTION));
    }

    @Test
    void validFixturesFollowEveryRule() {
        var valid = FIXTURES.that(DescribedPredicate.describe(
                "valid fixtures",
                type -> !type.getName().startsWith(InvalidUseCases.class.getName())
                        && !type.getName().startsWith(InvalidDomain.class.getName())
                        && !type.getName().equals(MisplacedUseCase.class.getName())));
        FIXTURE_RULES.all().forEach(rule -> rule.check(valid));
    }

    @Test
    void aUseCaseOutsideAnApplicationPackageIsRejected() {
        assertThatThrownBy(() -> FIXTURE_RULES.liveInApplicationPackages().check(FIXTURES))
                .hasMessageContaining(MisplacedUseCase.class.getName())
                .hasMessageContaining("(1 times)");
    }

    @Test
    void aUseCaseMarkedAsBothCommandAndQueryIsRejected() {
        assertThatThrownBy(() -> FIXTURE_RULES.areEitherCommandOrQuery().check(FIXTURES))
                .hasMessageContaining(InvalidUseCases.BothKinds.class.getName())
                .hasMessageContaining("(1 times)");
    }

    @Test
    void aUseCaseWithMoreThanOneOperationIsRejectedIncludingInheritedAndComposedCases() {
        assertThatThrownBy(() -> FIXTURE_RULES.haveOneOperation().check(FIXTURES))
                .hasMessageContaining(InvalidUseCases.TwoOperations.class.getName())
                .hasMessageContaining(InvalidUseCases.MetaAnnotatedTwoOperations.class.getName())
                .hasMessageContaining(InvalidUseCases.InheritsASecondOperation.class.getName())
                .hasMessageNotContaining(ValidUseCases.class.getName());
    }

    @Test
    void aFinalUseCaseOrOperationIsRejected() {
        assertThatThrownBy(() -> FIXTURE_RULES.areProxyable().check(FIXTURES))
                .hasMessageContaining(InvalidUseCases.FinalUseCase.class.getName())
                .hasMessageContaining(InvalidUseCases.FinalOperation.class.getName())
                .hasMessageContaining("(2 times)");
    }

    @Test
    void aUseCaseOutsideTheTransactionOfItsKindIsRejected() {
        assertThatThrownBy(() -> FIXTURE_RULES.runInTheTransactionOfTheirKind().check(FIXTURES))
                .hasMessageContaining(InvalidUseCases.NonTransactionalCommand.class.getName())
                .hasMessageContaining(InvalidUseCases.ReadOnlyCommand.class.getName())
                .hasMessageContaining(InvalidUseCases.NestedCommand.class.getName())
                .hasMessageContaining(InvalidUseCases.ReadWriteQuery.class.getName())
                .hasMessageContaining(InvalidUseCases.SupportsQuery.class.getName())
                .hasMessageNotContaining(ValidUseCases.class.getName());
    }

    @Test
    void domainCodeOutsideItsAllowListIsRejected() {
        assertThatThrownBy(() -> FIXTURE_RULES.domainDependsOnTheAllowListOnly().check(FIXTURES))
                .hasMessageContaining(InvalidDomain.SpringAwareTab.class.getName())
                .hasMessageContaining(InvalidDomain.TabOfOrder.class.getName())
                .hasMessageContaining(InvalidDomain.TabCallingItsUseCase.class.getName())
                .hasMessageNotContaining("<fixtures.tabs.domain.Tab.");
    }

    @Test
    void useCaseCodeOutsideItsAllowListIsRejected() {
        assertThatThrownBy(
                        () -> FIXTURE_RULES.useCasesDependOnTheAllowListOnly().check(FIXTURES))
                .hasMessageContaining(InvalidUseCases.ObservingCommand.class.getName())
                .hasMessageContaining(InvalidUseCases.UsesSpringUtility.class.getName())
                .hasMessageContaining(InvalidUseCases.UsesOwnInfrastructure.class.getName())
                .hasMessageContaining(InvalidUseCases.UsesOtherModuleDomain.class.getName())
                .hasMessageNotContaining(ValidUseCases.class.getName());
    }

    // Same methods the platform advises at runtime: public ones, inherited included, without Object's, bridge or
    // synthetic methods.
    private static Stream<Method> operationsOf(Class<?> useCase) {
        return Arrays.stream(useCase.getMethods())
                .filter(method -> method.getDeclaringClass() != Object.class)
                .filter(method -> !method.isBridge() && !method.isSynthetic());
    }

    /**
     * The rules for modules under one base package ({@code com.frappe.<module>} in production).
     *
     * @param basePackage the package whose direct subpackages are the modules
     */
    record UseCaseRules(String basePackage) {

        List<ArchRule> all() {
            return List.of(
                    liveInApplicationPackages(),
                    areEitherCommandOrQuery(),
                    haveOneOperation(),
                    areProxyable(),
                    runInTheTransactionOfTheirKind(),
                    domainDependsOnTheAllowListOnly(),
                    useCasesDependOnTheAllowListOnly());
        }

        ArchRule liveInApplicationPackages() {
            return classes()
                    .that(USE_CASES)
                    .should()
                    .resideInAPackage(basePackage + ".*.application..")
                    .allowEmptyShould(true);
        }

        ArchRule areEitherCommandOrQuery() {
            return classes()
                    .that(USE_CASES)
                    .should(ArchCondition.from(DescribedPredicate.<JavaClass>describe(
                            "carry exactly one of @CommandUseCase and @QueryUseCase",
                            type -> type.isMetaAnnotatedWith(CommandUseCase.class)
                                    != type.isMetaAnnotatedWith(QueryUseCase.class))))
                    .allowEmptyShould(true);
        }

        ArchRule haveOneOperation() {
            return classes()
                    .that(USE_CASES)
                    .should(ArchCondition.from(DescribedPredicate.<JavaClass>describe(
                            "have exactly one public method (inherited ones included)",
                            type -> operationsOf(type.reflect()).count() == 1)))
                    .allowEmptyShould(true);
        }

        ArchRule areProxyable() {
            return classes()
                    .that(USE_CASES)
                    .should(ArchCondition.from(DescribedPredicate.<JavaClass>describe(
                            "be proxyable by CGLIB (class and operation not final)",
                            type -> !Modifier.isFinal(type.reflect().getModifiers())
                                    && operationsOf(type.reflect())
                                            .noneMatch(method -> Modifier.isFinal(method.getModifiers())))))
                    .allowEmptyShould(true);
        }

        ArchRule runInTheTransactionOfTheirKind() {
            var transactions = new AnnotationTransactionAttributeSource();
            return classes()
                    .that(USE_CASES)
                    .should(
                            new ArchCondition<>("run their operation in the transaction of their kind (commands"
                                    + " read-write, queries read-only; propagation REQUIRED or REQUIRES_NEW)") {
                                @Override
                                public void check(JavaClass type, ConditionEvents events) {
                                    var useCase = type.reflect();
                                    var query = type.isMetaAnnotatedWith(QueryUseCase.class);
                                    operationsOf(useCase).forEach(method -> {
                                        var transaction = transactions.getTransactionAttribute(method, useCase);
                                        var satisfied = transaction != null
                                                && transaction.isReadOnly() == query
                                                && OWN_TRANSACTION_PROPAGATIONS.contains(
                                                        transaction.getPropagationBehavior());
                                        events.add(new SimpleConditionEvent(
                                                type,
                                                satisfied,
                                                useCase.getName() + "." + method.getName() + " must be @Transactional"
                                                        + (query ? "(readOnly = true)" : " (not read-only)")
                                                        + " with propagation REQUIRED or REQUIRES_NEW"));
                                    });
                                }
                            })
                    .allowEmptyShould(true);
        }

        ArchRule domainDependsOnTheAllowListOnly() {
            return classes()
                    .that()
                    .resideInAPackage(basePackage + ".*.domain..")
                    .should(dependOnlyOn(
                            "the JDK, the kernel and their own module's domain",
                            (origin, target) -> isJdkOrKernel(target) || isOwnModuleDomain(origin, target)))
                    .allowEmptyShould(true);
        }

        ArchRule useCasesDependOnTheAllowListOnly() {
            return classes()
                    .that()
                    .resideInAPackage(basePackage + ".*.application..")
                    .should(dependOnlyOn(
                            "the JDK, the kernel, their own module (not its infrastructure), other modules' root"
                                    + " packages and @Transactional / Propagation / Isolation",
                            (origin, target) -> isJdkOrKernel(target)
                                    || TRANSACTION_ANNOTATION_TYPES.contains(target.getName())
                                    || isOwnModuleOutsideInfrastructure(origin, target)
                                    || isAModuleRootPackage(target)))
                    .allowEmptyShould(true);
        }

        private boolean isOwnModuleDomain(JavaClass origin, JavaClass target) {
            var module = moduleOf(origin);
            return moduleOf(target).equals(module) && isInPackage(target, module + ".domain");
        }

        private boolean isOwnModuleOutsideInfrastructure(JavaClass origin, JavaClass target) {
            var module = moduleOf(origin);
            return moduleOf(target).equals(module) && !isInPackage(target, module + ".infrastructure");
        }

        private boolean isAModuleRootPackage(JavaClass target) {
            var module = moduleOf(target);
            return !module.isEmpty() && target.getPackageName().equals(module);
        }

        // The module package of a type: <base>.<module>, or "" outside the base package.
        private String moduleOf(JavaClass type) {
            var prefix = basePackage + ".";
            var packageName = type.getPackageName() + ".";
            if (!packageName.startsWith(prefix) || packageName.length() == prefix.length()) {
                return "";
            }
            return packageName.substring(0, packageName.indexOf('.', prefix.length()));
        }

        private static boolean isInPackage(JavaClass type, String packageName) {
            return type.getPackageName().equals(packageName)
                    || type.getPackageName().startsWith(packageName + ".");
        }

        private static boolean isJdkOrKernel(JavaClass target) {
            return target.isPrimitive()
                    || target.getPackageName().startsWith("java.")
                    || target.getPackageName().equals(KERNEL);
        }

        private static ArchCondition<JavaClass> dependOnlyOn(
                String description, BiPredicate<JavaClass, JavaClass> allowed) {
            return new ArchCondition<>("depend only on " + description) {
                @Override
                public void check(JavaClass type, ConditionEvents events) {
                    for (Dependency dependency : type.getDirectDependenciesFromSelf()) {
                        var target = dependency.getTargetClass().getBaseComponentType();
                        if (!allowed.test(type, target)) {
                            events.add(SimpleConditionEvent.violated(dependency, dependency.getDescription()));
                        }
                    }
                }
            };
        }
    }
}
