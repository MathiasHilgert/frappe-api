package com.frappe.platform.infrastructure.bus;

import com.frappe.platform.CommandBus;
import com.frappe.platform.QueryBus;
import io.micrometer.observation.ObservationRegistry;
import org.springframework.beans.factory.config.ConfigurableListableBeanFactory;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;

/**
 * Provides the command and query buses, validating every handler when the context starts. Only the observed buses are
 * beans, so no caller can bypass the observation.
 */
@Configuration(proxyBeanMethods = false)
class BusConfiguration {

    /** Creates the configuration; instantiated by Spring. */
    BusConfiguration() {}

    /**
     * Rolls back a command handler's transaction when it returns a failure. Static, as every bean post-processor, so
     * it is created before (and never delays) the configuration's other beans.
     *
     * @return the post-processor
     */
    @Bean
    static RollbackOnFailurePostProcessor rollbackOnFailurePostProcessor() {
        return new RollbackOnFailurePostProcessor();
    }

    /**
     * The observed command bus over every {@code CommandHandler} bean.
     *
     * @param beans the bean factory holding the handlers
     * @param observations where dispatches are observed
     * @return the command bus
     */
    @Bean
    CommandBus commandBus(ConfigurableListableBeanFactory beans, ObservationRegistry observations) {
        var handlers = HandlerRegistry.discover(beans, UseCaseKind.COMMAND);
        return new ObservedCommandBus(new HandlerCommandBus(handlers), new UseCaseObservations(observations));
    }

    /**
     * The observed query bus over every {@code QueryHandler} bean.
     *
     * @param beans the bean factory holding the handlers
     * @param observations where queries are observed
     * @return the query bus
     */
    @Bean
    QueryBus queryBus(ConfigurableListableBeanFactory beans, ObservationRegistry observations) {
        var handlers = HandlerRegistry.discover(beans, UseCaseKind.QUERY);
        return new ObservedQueryBus(new HandlerQueryBus(handlers), new UseCaseObservations(observations));
    }
}
