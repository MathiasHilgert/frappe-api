package com.frappe.platform.infrastructure.web;

import com.frappe.platform.web.Access;
import com.frappe.platform.web.Posture;
import org.slf4j.LoggerFactory;
import org.springframework.boot.test.context.TestConfiguration;
import org.springframework.jdbc.core.simple.JdbcClient;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.RestController;

/** A public route that logs and queries the database, for tests of request telemetry; importing this registers it. */
@TestConfiguration(proxyBeanMethods = false)
public class ProbeRoutes {

    /** The probe's path, without the {@code /v1} prefix. */
    public static final String PROBE_PATH = "/test/observability-probe";

    /** Logs one line and runs one query, so a request produces a log record and a database span. */
    @RestController
    @Access(Posture.PUBLIC)
    public static class ProbeController {

        private final JdbcClient jdbc;

        ProbeController(JdbcClient jdbc) {
            this.jdbc = jdbc;
        }

        /**
         * Probes the database.
         *
         * @return 1
         */
        @GetMapping(PROBE_PATH)
        public Integer probe() {
            LoggerFactory.getLogger(ProbeController.class).info("Probing the database");
            return jdbc.sql("select 1").query(Integer.class).single();
        }
    }
}
