package com.frappe.platform.infrastructure.scheduling;

import com.frappe.platform.EntityTask;
import com.frappe.platform.OneTimeTask;
import com.frappe.platform.ScheduledTasks;
import com.frappe.platform.TaskName;
import org.springframework.boot.test.context.TestConfiguration;
import org.springframework.context.annotation.Bean;

/**
 * Tasks declared in the application context the way modules declare theirs, for tests of the application's own
 * scheduler. Only contexts importing this configuration know them; other cached contexts leave their rows alone. Tests
 * using it run with {@code db-scheduler.polling-interval=100ms}, {@code frappe.scheduling.initial-backoff=200ms} and
 * {@code frappe.scheduling.max-retries=1}, so they share one context.
 */
@TestConfiguration(proxyBeanMethods = false)
class SchedulingProbes {

    static final TaskName FAILING = TaskName.of("platform.failing-probe");

    static final TaskName QUIET = TaskName.of("platform.quiet-probe");

    static final TaskName ENTITY = TaskName.of("platform.entity-probe");

    @Bean
    OneTimeTask<String> failingProbe(ScheduledTasks tasks) {
        return tasks.oneTime(FAILING, String.class, key -> {
            throw new IllegalStateException("probe failure " + key);
        });
    }

    @Bean
    OneTimeTask<String> quietProbe(ScheduledTasks tasks) {
        return tasks.oneTime(QUIET, String.class, key -> {});
    }

    @Bean
    EntityTask entityProbe(ScheduledTasks tasks) {
        return tasks.perEntity(ENTITY, entityId -> {});
    }
}
