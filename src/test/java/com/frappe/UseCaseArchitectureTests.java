package com.frappe;

import static com.tngtech.archunit.core.domain.JavaClass.Predicates.resideInAPackage;
import static com.tngtech.archunit.core.domain.JavaClass.Predicates.resideInAnyPackage;
import static com.tngtech.archunit.lang.syntax.ArchRuleDefinition.classes;
import static com.tngtech.archunit.lang.syntax.ArchRuleDefinition.noClasses;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

import com.frappe.platform.CommandUseCase;
import com.frappe.platform.QueryUseCase;
import com.tngtech.archunit.base.DescribedPredicate;
import com.tngtech.archunit.core.domain.JavaClass;
import com.tngtech.archunit.core.domain.JavaClasses;
import com.tngtech.archunit.core.domain.JavaModifier;
import com.tngtech.archunit.core.importer.ClassFileImporter;
import com.tngtech.archunit.core.importer.ImportOption;
import com.tngtech.archunit.lang.ArchCondition;
import com.tngtech.archunit.lang.ArchRule;
import com.tngtech.archunit.lang.ConditionEvents;
import com.tngtech.archunit.lang.SimpleConditionEvent;
import fixtures.usecases.application.InvalidUseCases;
import fixtures.usecases.domain.SpringAwareTab;
import fixtures.usecases.web.MisplacedUseCase;
import java.lang.reflect.Modifier;
import java.util.Arrays;
import java.util.Set;
import org.junit.jupiter.api.Test;
import org.springframework.transaction.TransactionDefinition;
import org.springframework.transaction.annotation.AnnotationTransactionAttributeSource;
import org.springframework.util.ReflectionUtils;

/**
 * Use cases follow one shape: a class per operation in the module's {@code application} package, marked with exactly
 * one kernel stereotype, whose operation runs in the transaction of its kind ({@code @CommandUseCase}: read-write,
 * {@code @QueryUseCase}: read-only). Domain, kernel and use-case code stay free of infrastructure libraries; the one
 * accepted Spring annotation in use-case code is {@code @Transactional}. Every rule is proven against fixtures that
 * break it and checked against production code.
 */
class UseCaseArchitectureTests {

    private static final DescribedPredicate<JavaClass> USE_CASES = DescribedPredicate.describe(
            "use cases",
            type -> type.isAnnotatedWith(CommandUseCase.class) || type.isAnnotatedWith(QueryUseCase.class));

    // Adapters (infrastructure) use these; domain, kernel and use cases never do.
    private static final String[] INFRASTRUCTURE_LIBRARIES = {
        "org.springframework..",
        "io.micrometer..",
        "io.opentelemetry..",
        "io.github.bucket4j..",
        "com.github.kagkarlsson..",
        "io.nats..",
        "io.lettuce..",
        "com.ibm.icu..",
        "jakarta.persistence..",
        "tools.jackson..",
        "com.fasterxml.jackson.."
    };

    // The one accepted exception in use cases: @Transactional (with its Propagation / Isolation attribute types).
    private static final String TRANSACTION_ANNOTATIONS = "org.springframework.transaction.annotation";

    private static final Set<Integer> OWN_TRANSACTION_PROPAGATIONS = Set.of(
            TransactionDefinition.PROPAGATION_REQUIRED,
            TransactionDefinition.PROPAGATION_REQUIRES_NEW,
            TransactionDefinition.PROPAGATION_NESTED);

    private static final ArchRule LIVE_IN_APPLICATION_PACKAGES = classes()
            .that(USE_CASES)
            .should()
            .resideInAPackage("..application..")
            .allowEmptyShould(true);

    private static final ArchRule ARE_EITHER_COMMAND_OR_QUERY = noClasses()
            .that()
            .areAnnotatedWith(CommandUseCase.class)
            .should()
            .beAnnotatedWith(QueryUseCase.class)
            .allowEmptyShould(true);

    private static final ArchRule HAVE_ONE_OPERATION = classes()
            .that(USE_CASES)
            .should(ArchCondition.from(DescribedPredicate.<JavaClass>describe(
                    "have exactly one public method",
                    type -> type.getMethods().stream()
                                    .filter(method -> method.getModifiers().contains(JavaModifier.PUBLIC))
                                    .count()
                            == 1)))
            .allowEmptyShould(true);

    private static final ArchRule RUN_IN_THE_TRANSACTION_OF_THEIR_KIND =
            classes().that(USE_CASES).should(runInTheTransactionOfTheirKind()).allowEmptyShould(true);

    private static final ArchRule DOMAIN_AND_KERNEL_ARE_TECHNOLOGY_FREE = noClasses()
            .that()
            .resideInAPackage("..domain..")
            .or()
            .resideInAPackage("com.frappe.platform")
            .should()
            .dependOnClassesThat()
            .resideInAnyPackage(INFRASTRUCTURE_LIBRARIES)
            .allowEmptyShould(true);

    private static final ArchRule USE_CASES_USE_NO_INFRASTRUCTURE_LIBRARY = noClasses()
            .that()
            .resideInAPackage("..application..")
            .should()
            .dependOnClassesThat(resideInAnyPackage(INFRASTRUCTURE_LIBRARIES)
                    .and(DescribedPredicate.not(resideInAPackage(TRANSACTION_ANNOTATIONS)))
                    .as("reside in an infrastructure library (other than @Transactional)"))
            .allowEmptyShould(true);

    private static final JavaClasses PRODUCTION = new ClassFileImporter()
            .withImportOption(ImportOption.Predefined.DO_NOT_INCLUDE_TESTS)
            .importPackages("com.frappe");

    private static final JavaClasses FIXTURES = new ClassFileImporter().importPackages("fixtures.usecases");

    @Test
    void productionCodeFollowsEveryRule() {
        LIVE_IN_APPLICATION_PACKAGES.check(PRODUCTION);
        ARE_EITHER_COMMAND_OR_QUERY.check(PRODUCTION);
        HAVE_ONE_OPERATION.check(PRODUCTION);
        RUN_IN_THE_TRANSACTION_OF_THEIR_KIND.check(PRODUCTION);
        DOMAIN_AND_KERNEL_ARE_TECHNOLOGY_FREE.check(PRODUCTION);
        USE_CASES_USE_NO_INFRASTRUCTURE_LIBRARY.check(PRODUCTION);
    }

    @Test
    void aUseCaseOutsideAnApplicationPackageIsRejected() {
        assertThatThrownBy(() -> LIVE_IN_APPLICATION_PACKAGES.check(FIXTURES))
                .hasMessageContaining(MisplacedUseCase.class.getName())
                .hasMessageContaining("(1 times)");
    }

    @Test
    void aUseCaseMarkedAsBothCommandAndQueryIsRejected() {
        assertThatThrownBy(() -> ARE_EITHER_COMMAND_OR_QUERY.check(FIXTURES))
                .hasMessageContaining(InvalidUseCases.BothKinds.class.getName())
                .hasMessageContaining("(1 times)");
    }

    @Test
    void aUseCaseWithMoreThanOneOperationIsRejected() {
        assertThatThrownBy(() -> HAVE_ONE_OPERATION.check(FIXTURES))
                .hasMessageContaining(InvalidUseCases.TwoOperations.class.getName())
                .hasMessageContaining("(1 times)");
    }

    @Test
    void aUseCaseOutsideTheTransactionOfItsKindIsRejected() {
        assertThatThrownBy(() -> RUN_IN_THE_TRANSACTION_OF_THEIR_KIND.check(FIXTURES))
                .hasMessageContaining(InvalidUseCases.NonTransactionalCommand.class.getName())
                .hasMessageContaining(InvalidUseCases.ReadOnlyCommand.class.getName())
                .hasMessageContaining(InvalidUseCases.ReadWriteQuery.class.getName())
                .hasMessageContaining(InvalidUseCases.SupportsQuery.class.getName())
                .hasMessageNotContaining("ValidUseCases");
    }

    @Test
    void domainCodeThatDependsOnSpringIsRejected() {
        assertThatThrownBy(() -> DOMAIN_AND_KERNEL_ARE_TECHNOLOGY_FREE.check(FIXTURES))
                .hasMessageContaining(SpringAwareTab.class.getName());
    }

    @Test
    void useCaseCodeThatDependsOnAnInfrastructureLibraryIsRejected() {
        assertThatThrownBy(() -> USE_CASES_USE_NO_INFRASTRUCTURE_LIBRARY.check(FIXTURES))
                .hasMessageContaining(InvalidUseCases.ObservingCommand.class.getName())
                .hasMessageContaining("io.micrometer.observation.ObservationRegistry")
                .hasMessageNotContaining("ValidUseCases")
                .hasMessageContaining("was violated (2 times)");
    }

    // Asks Spring's own transaction attribute source, so the rule sees exactly what the transaction proxy applies
    // (method before class, meta-annotations, propagation, read-only flag).
    private static ArchCondition<JavaClass> runInTheTransactionOfTheirKind() {
        var transactions = new AnnotationTransactionAttributeSource();
        return new ArchCondition<>("run their operation in a transaction of their kind (commands read-write, queries"
                + " read-only; propagation REQUIRED, REQUIRES_NEW or NESTED)") {
            @Override
            public void check(JavaClass type, ConditionEvents events) {
                var useCase = type.reflect();
                var query = type.isAnnotatedWith(QueryUseCase.class);
                Arrays.stream(useCase.getDeclaredMethods())
                        .filter(method -> Modifier.isPublic(method.getModifiers()))
                        .filter(method -> !ReflectionUtils.isObjectMethod(method))
                        .forEach(method -> {
                            var transaction = transactions.getTransactionAttribute(method, useCase);
                            var satisfied = transaction != null
                                    && transaction.isReadOnly() == query
                                    && OWN_TRANSACTION_PROPAGATIONS.contains(transaction.getPropagationBehavior());
                            events.add(new SimpleConditionEvent(
                                    type,
                                    satisfied,
                                    useCase.getName() + "." + method.getName() + " must be @Transactional"
                                            + (query ? "(readOnly = true)" : " (not read-only)")
                                            + " with propagation REQUIRED, REQUIRES_NEW or NESTED"));
                        });
            }
        };
    }
}
