package com.frappe.platform.infrastructure.observability;

import static org.assertj.core.api.Assertions.assertThat;
import static org.awaitility.Awaitility.await;

import com.frappe.TestNatsConfiguration;
import com.frappe.TestcontainersConfiguration;
import io.opentelemetry.sdk.logs.data.LogRecordData;
import io.opentelemetry.sdk.testing.exporter.InMemoryLogRecordExporter;
import java.time.Duration;
import org.junit.jupiter.api.Test;
import org.slf4j.LoggerFactory;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.boot.test.context.TestConfiguration;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Import;
import org.springframework.test.context.ActiveProfiles;

/** Application log lines leave the process as OTLP log records (Loki locally), next to the ECS console output. */
@SpringBootTest(properties = "management.opentelemetry.logging.export.schedule-delay=50ms")
@Import({TestcontainersConfiguration.class, TestNatsConfiguration.class, OtlpLogExportTests.Exporter.class})
@ActiveProfiles("local")
class OtlpLogExportTests {

    @TestConfiguration(proxyBeanMethods = false)
    static class Exporter {

        @Bean
        InMemoryLogRecordExporter inMemoryLogRecordExporter() {
            return InMemoryLogRecordExporter.create();
        }
    }

    @Autowired
    InMemoryLogRecordExporter logs;

    @Test
    void exportsApplicationLogLinesAsOtlpLogRecords() {
        // When
        LoggerFactory.getLogger(OtlpLogExportTests.class).info("Exported over OTLP");

        // Then
        await().atMost(Duration.ofSeconds(10))
                .untilAsserted(() -> assertThat(logs.getFinishedLogRecordItems())
                        .extracting(LogRecordData::getBodyValue)
                        .extracting(String::valueOf)
                        .anyMatch(body -> body.contains("Exported over OTLP")));
    }
}
