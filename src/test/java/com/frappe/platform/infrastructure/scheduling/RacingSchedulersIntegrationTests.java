package com.frappe.platform.infrastructure.scheduling;

import static org.assertj.core.api.Assertions.assertThat;
import static org.awaitility.Awaitility.await;

import com.frappe.TestcontainersConfiguration;
import com.frappe.platform.EntitySchedule;
import com.github.kagkarlsson.scheduler.Scheduler;
import com.github.kagkarlsson.scheduler.SchedulerClient.ScheduleOptions;
import com.github.kagkarlsson.scheduler.SchedulerName;
import com.github.kagkarlsson.scheduler.boot.autoconfigure.Jackson3Serializer;
import com.github.kagkarlsson.scheduler.task.ExecutionComplete;
import com.github.kagkarlsson.scheduler.task.OnStartup;
import com.github.kagkarlsson.scheduler.task.Task;
import com.github.kagkarlsson.scheduler.task.TaskInstanceId;
import com.github.kagkarlsson.scheduler.task.schedule.FixedDelay;
import io.micrometer.observation.tck.TestObservationRegistry;
import io.micrometer.observation.tck.TestObservationRegistryAssert;
import java.time.Duration;
import java.time.Instant;
import java.time.ZoneId;
import java.util.ArrayList;
import java.util.List;
import java.util.Locale;
import java.util.UUID;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.CopyOnWriteArrayList;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicInteger;
import javax.sql.DataSource;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.context.annotation.Import;
import org.springframework.jdbc.core.simple.JdbcClient;
import org.springframework.test.context.ActiveProfiles;

/**
 * Two scheduler instances, built like the application's (same table, JSON data, observation and failure log), race for
 * the same executions on the real Postgres as the runtime role. Task names are unique per test, so the application's
 * own scheduler (which does not know them) never picks their rows; they are removed after each test.
 */
@SpringBootTest
@Import(TestcontainersConfiguration.class)
@ActiveProfiles("local")
class RacingSchedulersIntegrationTests {

    static final Duration POLLING = Duration.ofMillis(100);

    static final Duration HEARTBEAT = Duration.ofMillis(250);

    // The library's minimum: an execution is dead after 4 × 250ms without a heartbeat.
    static final int MISSED_HEARTBEATS = 4;

    static final Duration INITIAL_BACKOFF = Duration.ofMillis(300);

    final ConventionalScheduledTasks tasks =
            new ConventionalScheduledTasks(new SchedulingProperties(INITIAL_BACKOFF, 5));

    final TestObservationRegistry observations = TestObservationRegistry.create();

    final List<Scheduler> started = new ArrayList<>();

    final List<String> taskNames = new ArrayList<>();

    @Autowired
    DataSource dataSource;

    @Autowired
    JdbcClient jdbc;

    @AfterEach
    void stopSchedulersAndDeleteTheirExecutions() {
        started.forEach(Scheduler::stop);
        jdbc.sql("delete from platform.scheduled_tasks where task_name in (:names)")
                .param("names", taskNames)
                .update();
    }

    @Test
    void everyDueExecutionOfARecurringTaskRunsOnExactlyOneInstance() {
        // Given
        var ranDueExecutions = new CopyOnWriteArrayList<Instant>();
        var task = tasks.recurring(
                uniqueName("race"),
                FixedDelay.of(Duration.ofMillis(200)),
                (instance, context) -> ranDueExecutions.add(context.getExecution().executionTime));

        // When
        startInstance("instance-a", task);
        startInstance("instance-b", task);

        // Then
        await().atMost(Duration.ofSeconds(20)).until(() -> ranDueExecutions.size() >= 10);
        assertThat(ranDueExecutions).doesNotHaveDuplicates();
    }

    @Test
    void everyOneTimeExecutionRunsOnExactlyOneInstance() {
        // Given
        var runs = new ConcurrentHashMap<String, AtomicInteger>();
        var task = tasks.oneTime(
                uniqueName("race-once"),
                String.class,
                (instance, context) -> runs.computeIfAbsent(instance.getId(), id -> new AtomicInteger())
                        .incrementAndGet());
        var a = startInstance("instance-a", task);
        startInstance("instance-b", task);

        // When
        for (var order = 0; order < 50; order++) {
            a.scheduleIfNotExists(task.instance("order-" + order, "payload"), Instant.now());
        }

        // Then
        await().atMost(Duration.ofSeconds(20)).until(() -> runs.size() == 50);
        await().pollDelay(Duration.ofSeconds(1)).until(() -> true);
        assertThat(runs.values()).extracting(AtomicInteger::get).containsOnly(1);
    }

    @Test
    void anExecutionOfADeadInstanceIsTakenOverOnceItsHeartbeatExpires() throws Exception {
        // Given an execution running on instance A
        var runningOnA = new CountDownLatch(1);
        var releaseA = new CompletableFuture<Void>();
        var ranBy = new CopyOnWriteArrayList<String>();
        var task = tasks.oneTime(uniqueName("takeover"), String.class, (instance, context) -> {
            ranBy.add(context.getExecution().pickedBy);
            if ("instance-a".equals(context.getExecution().pickedBy)) {
                runningOnA.countDown();
                // A dead instance never finishes; join() ignores the interrupt of the scheduler's shutdown.
                releaseA.join();
            }
        });
        var a = startInstance("instance-a", task);
        a.scheduleIfNotExists(task.instance("close-day", "payload"), Instant.now());
        assertThat(runningOnA.await(10, TimeUnit.SECONDS)).isTrue();

        try {
            // When A dies (stops heartbeating with the execution still picked) and B runs
            a.stop();
            startInstance("instance-b", task);

            // Then
            await().atMost(Duration.ofSeconds(20)).until(() -> ranBy.contains("instance-b"));
            assertThat(ranBy).containsExactly("instance-a", "instance-b");
        } finally {
            releaseA.complete(null);
        }
    }

    @Test
    void aFailingTaskRetriesWithExponentialBackoffAndEveryFailureIsObservedAsAnError() {
        // Given
        var attempts = new CopyOnWriteArrayList<Instant>();
        var task = tasks.oneTime(uniqueName("flaky"), String.class, (instance, context) -> {
            attempts.add(Instant.now());
            if (attempts.size() <= 3) {
                throw new IllegalStateException("attempt " + attempts.size() + " failed");
            }
        });
        var a = startInstance("instance-a", task);

        // When
        a.scheduleIfNotExists(task.instance("report-2026-09", "payload"), Instant.now());

        // Then
        await().atMost(Duration.ofSeconds(20)).until(() -> attempts.size() == 4);
        assertThat(Duration.between(attempts.get(0), attempts.get(1))).isGreaterThanOrEqualTo(INITIAL_BACKOFF);
        assertThat(Duration.between(attempts.get(1), attempts.get(2)))
                .isGreaterThanOrEqualTo(INITIAL_BACKOFF.multipliedBy(2));
        assertThat(Duration.between(attempts.get(2), attempts.get(3)))
                .isGreaterThanOrEqualTo(INITIAL_BACKOFF.multipliedBy(4));
        await().atMost(Duration.ofSeconds(5))
                .untilAsserted(() -> TestObservationRegistryAssert.assertThat(observations)
                        .hasNumberOfObservationsWithNameEqualTo("scheduled.task", 4)
                        .hasHandledContextsThatSatisfy(contexts -> {
                            assertThat(contexts)
                                    .filteredOn(context -> context.getError() != null)
                                    .hasSize(3)
                                    .allSatisfy(context -> assertThat(
                                                    context.getLowCardinalityKeyValue("scheduled.task.outcome")
                                                            .getValue())
                                            .isEqualTo("failure"));
                            assertThat(contexts)
                                    .filteredOn(context -> context.getError() == null)
                                    .singleElement()
                                    .satisfies(context -> assertThat(
                                                    context.getLowCardinalityKeyValue("scheduled.task.outcome")
                                                            .getValue())
                                            .isEqualTo("success"));
                        }));
    }

    @Test
    void aChangedEntityScheduleDrivesTheNextExecutionWithoutARestart() {
        // Given a branch closing its day at 04:00 in São Paulo
        var closedBranches = new CopyOnWriteArrayList<String>();
        var task =
                tasks.perEntity(uniqueName("close-day"), (instance, context) -> closedBranches.add(instance.getId()));
        var a = startInstance("instance-a", task);
        var saoPaulo = new EntitySchedule("0 0 4 * * *", ZoneId.of("America/Sao_Paulo"));
        a.schedule(task.schedulableInstance("branch-1", saoPaulo), ScheduleOptions.WHEN_EXISTS_RESCHEDULE);

        // When the branch moves to Berlin
        var berlin = saoPaulo.withZone(ZoneId.of("Europe/Berlin"));
        a.schedule(task.schedulableInstance("branch-1", berlin), ScheduleOptions.WHEN_EXISTS_RESCHEDULE);

        // Then the next execution is 04:00 in Berlin, and a schedule that is due runs on the running instance
        var scheduled = a.getScheduledExecution(TaskInstanceId.of(task.getName(), "branch-1"))
                .orElseThrow();
        assertThat(scheduled.getExecutionTime())
                .isEqualTo(
                        berlin.getSchedule().getNextExecutionTime(ExecutionComplete.simulatedSuccess(Instant.now())));
        assertThat(scheduled.getData()).isEqualTo(berlin);
        a.schedule(
                task.schedulableInstance("branch-1", new EntitySchedule("* * * * * *", ZoneId.of("Europe/Berlin"))),
                ScheduleOptions.WHEN_EXISTS_RESCHEDULE);
        await().atMost(Duration.ofSeconds(10)).until(() -> closedBranches.contains("branch-1"));
    }

    private String uniqueName(String prefix) {
        var name = "platform." + prefix + "-"
                + UUID.randomUUID().toString().substring(0, 8).toLowerCase(Locale.ROOT);
        taskNames.add(name);
        return name;
    }

    // Built like DbSchedulerConfigurationSupport builds the application's scheduler; fast timings for the test.
    @SuppressWarnings({"unchecked", "rawtypes"})
    private Scheduler startInstance(String name, Task<?> task) {
        var builder = Scheduler.create(dataSource, task instanceof OnStartup ? List.of() : List.of(task))
                .tableName("platform.scheduled_tasks")
                .schedulerName(new SchedulerName.Fixed(name))
                .serializer(new Jackson3Serializer())
                .pollingInterval(POLLING)
                .heartbeatInterval(HEARTBEAT)
                .missedHeartbeatsLimit(MISSED_HEARTBEATS)
                .shutdownMaxWait(Duration.ofMillis(200))
                .addExecutionInterceptor(new ObservedTaskExecution(observations))
                .addSchedulerListener(new TaskFailureLog(new SchedulingProperties(INITIAL_BACKOFF, 5)));
        if (task instanceof OnStartup) {
            builder.startTasks((List) List.of(task));
        }
        var scheduler = builder.build();
        scheduler.start();
        started.add(scheduler);
        return scheduler;
    }
}
