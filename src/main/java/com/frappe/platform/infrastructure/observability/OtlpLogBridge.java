package com.frappe.platform.infrastructure.observability;

import ch.qos.logback.classic.Logger;
import ch.qos.logback.classic.LoggerContext;
import io.opentelemetry.api.OpenTelemetry;
import io.opentelemetry.instrumentation.logback.appender.v1_0.OpenTelemetryAppender;
import org.slf4j.LoggerFactory;
import org.springframework.beans.factory.DisposableBean;
import org.springframework.beans.factory.SmartInitializingSingleton;

/**
 * Sends every Logback event to Spring Boot's OpenTelemetry {@code SdkLoggerProvider}, which exports it over OTLP (Loki
 * locally). The console appender and its ECS JSON stay untouched. Export is batched on its own thread and dropped when
 * no receiver is reachable, so logging never blocks or fails on it.
 */
class OtlpLogBridge implements SmartInitializingSingleton, DisposableBean {

    private final OpenTelemetryAppender appender = new OpenTelemetryAppender();
    private final OpenTelemetry openTelemetry;

    /**
     * Creates the bridge; it attaches once all singletons exist.
     *
     * @param openTelemetry Boot's OpenTelemetry SDK
     */
    OtlpLogBridge(OpenTelemetry openTelemetry) {
        this.openTelemetry = openTelemetry;
    }

    @Override
    public void afterSingletonsInstantiated() {
        // Attached here, after Boot's logging system initialized Logback, and detached on close: cached test contexts
        // share the JVM-wide Logback context.
        var context = (LoggerContext) LoggerFactory.getILoggerFactory();
        appender.setContext(context);
        appender.setName("OTLP");
        appender.setOpenTelemetry(openTelemetry);
        appender.setCaptureKeyValuePairAttributes(true);
        appender.start();
        root(context).addAppender(appender);
    }

    @Override
    public void destroy() {
        root((LoggerContext) LoggerFactory.getILoggerFactory()).detachAppender(appender);
        appender.stop();
    }

    private static Logger root(LoggerContext context) {
        return context.getLogger(org.slf4j.Logger.ROOT_LOGGER_NAME);
    }
}
