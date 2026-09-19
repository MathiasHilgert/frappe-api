package com.frappe.platform.infrastructure.usecase;

import io.micrometer.observation.ObservationRegistry;
import org.springframework.aop.Advisor;
import org.springframework.aop.support.DefaultPointcutAdvisor;
import org.springframework.beans.factory.ObjectProvider;
import org.springframework.beans.factory.config.BeanDefinition;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.context.annotation.Role;
import org.springframework.core.Ordered;
import org.springframework.core.env.Environment;
import org.springframework.core.io.ResourceLoader;
import org.springframework.transaction.annotation.EnableTransactionManagement;

/**
 * Wires use cases: registers them as beans and wraps their operations, from the outside in, in the observation, the
 * transaction and the rollback on a failure. The order is explicit: this configuration enables transaction management
 * itself (Spring Boot's own {@code @EnableTransactionManagement} backs off), with the transaction advisor between the
 * two use-case advisors. A lower order runs further outside.
 */
@Configuration(proxyBeanMethods = false)
@EnableTransactionManagement(proxyTargetClass = true, order = UseCaseConfiguration.TRANSACTION_ORDER)
class UseCaseConfiguration {

    /** Order of the observation advisor: outside the transaction, so the commit is observed. */
    static final int OBSERVATION_ORDER = Ordered.LOWEST_PRECEDENCE - 300;

    /** Order of every {@code @Transactional} advisor of the application. */
    static final int TRANSACTION_ORDER = Ordered.LOWEST_PRECEDENCE - 200;

    /** Order of the rollback advisor: inside the transaction, so it runs before the commit. */
    static final int ROLLBACK_ON_FAILURE_ORDER = Ordered.LOWEST_PRECEDENCE - 100;

    /** Creates the configuration; instantiated by Spring. */
    UseCaseConfiguration() {}

    /**
     * Registers the use cases of the application packages as beans. Static and fed only by the environment and the
     * resource loader (resolvable without creating beans), as a registry post-processor must be.
     *
     * @param environment the application environment
     * @param resourceLoader the application context's resource loader
     * @return the registrar
     */
    @Bean
    static UseCaseRegistrar useCaseRegistrar(Environment environment, ResourceLoader resourceLoader) {
        return new UseCaseRegistrar(environment, resourceLoader);
    }

    /**
     * Observes every use case operation.
     *
     * @param observations the observation registry, resolved on first call
     * @return the advisor
     */
    @Bean
    @Role(BeanDefinition.ROLE_INFRASTRUCTURE)
    static Advisor useCaseObservationAdvisor(ObjectProvider<ObservationRegistry> observations) {
        var advisor = new DefaultPointcutAdvisor(
                UseCasePointcut.operations(),
                new UseCaseObservationInterceptor(() -> observations.getIfAvailable(() -> ObservationRegistry.NOOP)));
        advisor.setOrder(OBSERVATION_ORDER);
        return advisor;
    }

    /**
     * Rolls back the use case's own transaction when an operation returns a failure.
     *
     * @return the advisor
     */
    @Bean
    @Role(BeanDefinition.ROLE_INFRASTRUCTURE)
    static Advisor useCaseRollbackOnFailureAdvisor() {
        var advisor = new DefaultPointcutAdvisor(
                UseCasePointcut.resultReturningOperations(), new RollbackOnFailureInterceptor());
        advisor.setOrder(ROLLBACK_ON_FAILURE_ORDER);
        return advisor;
    }
}
