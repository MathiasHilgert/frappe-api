package com.frappe;

import static com.tngtech.archunit.lang.syntax.ArchRuleDefinition.methods;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

import com.frappe.platform.TenantScope;
import com.tngtech.archunit.base.DescribedPredicate;
import com.tngtech.archunit.core.domain.JavaClass;
import com.tngtech.archunit.core.domain.JavaClasses;
import com.tngtech.archunit.core.domain.JavaMethod;
import com.tngtech.archunit.core.importer.ClassFileImporter;
import com.tngtech.archunit.lang.ArchCondition;
import com.tngtech.archunit.lang.ArchRule;
import com.tngtech.archunit.lang.ConditionEvents;
import com.tngtech.archunit.lang.SimpleConditionEvent;
import fixtures.tenancy.TenantListeners;
import org.junit.jupiter.api.Test;
import org.springframework.modulith.events.ApplicationModuleListener;
import org.springframework.transaction.annotation.Propagation;

/**
 * An event listener that binds a tenant through {@link TenantScope} opens no transaction of its own
 * ({@code propagation = NOT_SUPPORTED}): binding inside the default {@code REQUIRES_NEW} transaction throws on every
 * delivery, and the publication never completes. Checked on all of {@code com.frappe} (production and test modules)
 * and proven against fixtures that break it.
 */
class TenantListenerRulesTests {

    private static final JavaClasses APPLICATION = new ClassFileImporter().importPackages("com.frappe");

    private static final JavaClasses FIXTURES = new ClassFileImporter().importPackages("fixtures.tenancy");

    @Test
    void everyTenantBindingListenerOpensNoTransaction() {
        tenantListenersOpenNoTransaction().check(APPLICATION);
    }

    @Test
    void aTenantBindingListenerInATransactionIsRejected() {
        assertThatThrownBy(() -> tenantListenersOpenNoTransaction().check(FIXTURES))
                .hasMessageContaining(TenantListeners.Transactional.class.getName())
                .hasMessageNotContaining(TenantListeners.Compliant.class.getName())
                .hasMessageNotContaining(TenantListeners.Untenanted.class.getName());
    }

    static ArchRule tenantListenersOpenNoTransaction() {
        return methods()
                .that()
                .areAnnotatedWith(ApplicationModuleListener.class)
                .and()
                .areDeclaredInClassesThat(BINDS_A_TENANT)
                .should(OPEN_NO_TRANSACTION)
                .allowEmptyShould(true);
    }

    private static final DescribedPredicate<JavaClass> BINDS_A_TENANT = DescribedPredicate.describe(
            "use TenantScope",
            type -> type.getDirectDependenciesFromSelf().stream()
                    .anyMatch(dependency -> dependency.getTargetClass().isEquivalentTo(TenantScope.class)));

    private static final ArchCondition<JavaMethod> OPEN_NO_TRANSACTION =
            new ArchCondition<>("declare propagation = NOT_SUPPORTED") {
                @Override
                public void check(JavaMethod method, ConditionEvents events) {
                    var propagation = method.getAnnotationOfType(ApplicationModuleListener.class)
                            .propagation();
                    if (propagation != Propagation.NOT_SUPPORTED) {
                        events.add(SimpleConditionEvent.violated(
                                method,
                                method.getFullName() + " binds a tenant inside a " + propagation
                                        + " transaction; declare propagation = NOT_SUPPORTED"));
                    }
                }
            };
}
