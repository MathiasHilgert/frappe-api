package com.frappe;

import ch.qos.logback.classic.spi.ILoggingEvent;
import ch.qos.logback.core.filter.Filter;
import ch.qos.logback.core.read.ListAppender;
import ch.qos.logback.core.spi.FilterReply;
import java.util.function.Predicate;

/**
 * Log capture for assertions on the root logger, scoped to the threads doing the work under test. Other contexts in
 * Spring's test context cache keep running in the background of the same JVM (a NATS client reconnecting to a stopped
 * container logs ERROR on its own thread), so an unscoped capture asserts on unrelated work.
 */
public final class CapturedLogs {

    private CapturedLogs() {}

    /**
     * A started appender that keeps only events logged on matching threads.
     *
     * @param threadName which thread names to keep
     * @return the appender; attach it to a logger
     */
    public static ListAppender<ILoggingEvent> fromThreads(Predicate<String> threadName) {
        var logs = new ListAppender<ILoggingEvent>();
        logs.addFilter(new Filter<>() {
            @Override
            public FilterReply decide(ILoggingEvent event) {
                return threadName.test(event.getThreadName()) ? FilterReply.NEUTRAL : FilterReply.DENY;
            }
        });
        logs.start();
        return logs;
    }

    /**
     * A started appender that keeps only events logged on the calling thread (unit tests that run the code directly).
     *
     * @return the appender
     */
    public static ListAppender<ILoggingEvent> fromCurrentThread() {
        var current = Thread.currentThread().getName();
        return fromThreads(current::equals);
    }

    /**
     * The threads of embedded Tomcat servers ({@code http-nio-auto-<n>-exec-*} on a random port): only the server of
     * the test's own context receives requests, the servers of cached contexts sit idle.
     *
     * @return the thread name predicate
     */
    public static Predicate<String> serverThreads() {
        return name -> name.startsWith("http-nio-");
    }
}
