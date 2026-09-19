package com.frappe.platform.infrastructure.bus;

import com.frappe.platform.CommandBus;
import com.frappe.platform.QueryBus;
import org.springframework.beans.factory.ListableBeanFactory;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;

/** Provides the command and query buses, validating every handler when the context starts. */
@Configuration(proxyBeanMethods = false)
class BusConfiguration {

    /** Creates the configuration; instantiated by Spring. */
    BusConfiguration() {}

    /**
     * The command bus over every {@code CommandHandler} bean.
     *
     * @param beans the bean factory holding the handlers
     * @return the command bus
     */
    @Bean
    CommandBus commandBus(ListableBeanFactory beans) {
        return new HandlerCommandBus(HandlerRegistry.discover(beans, UseCaseKind.COMMAND));
    }

    /**
     * The query bus over every {@code QueryHandler} bean.
     *
     * @param beans the bean factory holding the handlers
     * @return the query bus
     */
    @Bean
    QueryBus queryBus(ListableBeanFactory beans) {
        return new HandlerQueryBus(HandlerRegistry.discover(beans, UseCaseKind.QUERY));
    }
}
