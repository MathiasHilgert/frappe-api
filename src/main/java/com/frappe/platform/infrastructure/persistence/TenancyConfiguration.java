package com.frappe.platform.infrastructure.persistence;

import com.frappe.platform.TenantScope;
import javax.sql.DataSource;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.transaction.TransactionExecutionListener;

/**
 * Binds transactions to the thread's tenant for row-level security. Spring Boot adds every
 * {@link TransactionExecutionListener} bean to the application's transaction manager.
 */
@Configuration(proxyBeanMethods = false)
class TenancyConfiguration {

    /** Creates the configuration; instantiated by Spring. */
    TenancyConfiguration() {}

    /**
     * The tenant bound to each thread.
     *
     * @return the tenant scope
     */
    @Bean
    TenantScope tenantScope() {
        return new ThreadBoundTenantScope();
    }

    /**
     * Sets the bound tenant at the start of every transaction.
     *
     * @param tenants the tenant scope
     * @param dataSource the application's data source, whose transactional connection the setting goes to
     * @return the listener
     */
    @Bean
    TransactionExecutionListener tenantTransactionListener(TenantScope tenants, DataSource dataSource) {
        return new TenantTransactionListener(tenants, new JdbcTemplate(dataSource));
    }
}
