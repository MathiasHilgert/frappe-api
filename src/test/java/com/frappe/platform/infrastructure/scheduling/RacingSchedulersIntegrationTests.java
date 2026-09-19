package com.frappe.platform.infrastructure.scheduling;

import static org.assertj.core.api.Assertions.assertThat;
import static org.awaitility.Awaitility.await;

import com.frappe.TestcontainersConfiguration;
import com.frappe.platform.EntitySchedule;
import com.frappe.platform.ScheduledTask;
import com.frappe.platform.TaskName;
import com.frappe.platform.TaskSchedule;
import com.github.kagkarlsson.scheduler.Scheduler;
import com.github.kagkarlsson.scheduler.SchedulerName;
import com.github.kagkarlsson.scheduler.boot.autoconfigure.Jackson3Serializer;
import com.github.kagkarlsson.scheduler.event.ExecutionInterceptor;
import com.github.kagkarlsson.scheduler.task.ExecutionComplete;
import com.github.kagkarlsson.scheduler.task.OnStartup;
import com.github.kagkarlsson.scheduler.task.Task;
import io.micrometer.core.instrument.simple.SimpleMeterRegistry;
import io.micrometer.observation.tck.TestObservationRegistry;
import io.micrometer.observation.tck.TestObservationRegistryAssert;
import java.time.Clock;
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
import java.util.concurrent.atomic.AtomicReference;
import javax.sql.DataSource;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.context.annotation.Import;
import org.springframework.jdbc.core.simple.JdbcClient;
import org.springframework.test.context.ActiveProfiles;

/**
 * Two scheduler instances, built like the application's (same table, JSON data, observation), race for the same
 * executions on the real Postgres as the runtime role. Tasks are declared through the platform's conventions; instance
 * A serves their scheduling calls. Task names are unique per test, so the application's own scheduler (which does not
 * know them) never picks their rows; they are removed after each test.
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

    final AtomicReference<Scheduler> instanceA = new AtomicReference<>();

    final SimpleMeterRegistry meters = new SimpleMeterRegistry();

    final ConventionalScheduledTasks tasks = new ConventionalScheduledTasks(
            new SchedulingProperties(INITIAL_BACKOFF, 5), instanceA::get, Clock.systemUTC(), meters);

    final TestObservationRegistry observations = TestObservationRegistry.create();

    // Recorded around every execution: which instance picked it (by task instance id), and which due execution it was
    // (by task name).
    final ConcurrentHashMap<String, List<String>> pickedBy = new ConcurrentHashMap<>();

    final ConcurrentHashMap<String, List<Instant>> ranExecutionTimes = new ConcurrentHashMap<>();

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
        var runs = new AtomicInteger();
        var task = tasks.recurring(
                uniqueName("race"), TaskSchedule.fixedDelay(Duration.ofMillis(200)), runs::incrementAndGet);

        // When
        startInstance("instance-a", task);
        startInstance("instance-b", task);

        // Then
        await().atMost(Duration.ofSeconds(20)).until(() -> runs.get() >= 10);
        assertThat(ranExecutionTimes.get(task.name().value())).doesNotHaveDuplicates();
    }

    @Test
    void everyOneTimeExecutionRunsOnExactlyOneInstanceAndBothInstancesTakeTheirShare() {
        // Given
        var runs = new ConcurrentHashMap<String, AtomicInteger>();
        var task = tasks.oneTime(uniqueName("race-once"), String.class, order -> {
            runs.computeIfAbsent(order, key -> new AtomicInteger()).incrementAndGet();
            // Slow enough that one instance cannot drain the batch before the other polls.
            sleep(Duration.ofMillis(50));
        });
        startInstance("instance-a", task);
        startInstance("instance-b", task);

        // When
        for (var order = 0; order < 100; order++) {
            task.schedule("order-" + order, "order-" + order, Instant.now());
        }

        // Then
        await().atMost(Duration.ofSeconds(30)).until(() -> runs.size() == 100);
        await().pollDelay(Duration.ofSeconds(1)).until(() -> true);
        assertThat(runs.values()).extracting(AtomicInteger::get).containsOnly(1);
        assertThat(pickedBy.values().stream().flatMap(List::stream).distinct())
                .containsExactlyInAnyOrder("instance-a", "instance-b");
    }

    @Test
    void anExecutionOfADeadInstanceIsTakenOverOnceItsHeartbeatExpires() throws Exception {
        // Given an execution running on instance A (B is not started yet)
        var runningOnA = new CountDownLatch(1);
        var releaseA = new CompletableFuture<Void>();
        var task = tasks.oneTime(uniqueName("takeover"), String.class, data -> {
            if (runningOnA.getCount() > 0) {
                runningOnA.countDown();
                // A dead instance never finishes; join() ignores the interrupt of the scheduler's shutdown.
                releaseA.join();
            }
        });
        var a = startInstance("instance-a", task);
        task.schedule("close-day", "payload", Instant.now());
        assertThat(runningOnA.await(10, TimeUnit.SECONDS)).isTrue();

        try {
            // When A dies (stops heartbeating with the execution still picked) and B runs
            a.stop();
            startInstance("instance-b", task);

            // Then
            await().atMost(Duration.ofSeconds(20))
                    .until(() -> pickedBy.getOrDefault("close-day", List.of()).contains("instance-b"));
            assertThat(pickedBy.get("close-day")).containsExactly("instance-a", "instance-b");
        } finally {
            releaseA.complete(null);
        }
    }

    @Test
    void aFailingTaskRetriesWithExponentialBackoffAndEveryFailureIsObservedAsAnError() {
        // Given
        var attempts = new CopyOnWriteArrayList<Instant>();
        var task = tasks.oneTime(uniqueName("flaky"), String.class, data -> {
            attempts.add(Instant.now());
            if (attempts.size() <= 3) {
                throw new IllegalStateException("attempt " + attempts.size() + " failed");
            }
        });
        startInstance("instance-a", task);

        // When
        task.schedule("report-2026-09", "payload", Instant.now());

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
    void aOneTimeTaskThatKeepsFailingEndsAfterItsRetriesAndIsCounted() {
        // Given one retry only
        var attempts = new AtomicInteger();
        var oneRetry = new ConventionalScheduledTasks(
                new SchedulingProperties(INITIAL_BACKOFF, 1), instanceA::get, Clock.systemUTC(), meters);
        var name = uniqueName("doomed");
        var task = oneRetry.oneTime(name, String.class, data -> {
            attempts.incrementAndGet();
            throw new IllegalStateException("never works");
        });
        startInstance("instance-a", task);

        // When
        task.schedule("report-2026-10", "payload", Instant.now());

        // Then
        await().atMost(Duration.ofSeconds(20))
                .until(() -> meters.get("scheduled.task.exhausted")
                                .tag("scheduled.task.name", name.value())
                                .counter()
                                .count()
                        == 1.0);
        assertThat(attempts).hasValue(2);
        assertThat(jdbc.sql("select count(*) from platform.scheduled_tasks where task_name = ?")
                        .param(name.value())
                        .query(Long.class)
                        .single())
                .isZero();
    }

    @Test
    void aChangedEntityScheduleDrivesTheNextExecutionWithoutARestart() {
        // Given a branch closing its day at 04:00 in São Paulo
        var closedBranches = new CopyOnWriteArrayList<String>();
        var task = tasks.perEntity(uniqueName("close-day"), closedBranches::add);
        startInstance("instance-a", task);
        var saoPaulo = new EntitySchedule("0 0 4 * * *", ZoneId.of("America/Sao_Paulo"));
        task.schedule("branch-1", saoPaulo);

        // When the branch moves to Berlin
        var berlin = saoPaulo.withZone(ZoneId.of("Europe/Berlin"));
        task.schedule("branch-1", berlin);

        // Then the next execution is 04:00 in Berlin, and a schedule that is due runs on the running instance
        assertThat(task.nextRun("branch-1"))
                .contains(StoredEntitySchedule.of(berlin)
                        .getSchedule()
                        .getNextExecutionTime(ExecutionComplete.simulatedSuccess(Instant.now())));
        task.schedule("branch-1", new EntitySchedule("* * * * * *", ZoneId.of("Europe/Berlin")));
        await().atMost(Duration.ofSeconds(10)).until(() -> closedBranches.contains("branch-1"));
    }

    private TaskName uniqueName(String prefix) {
        var name = "platform." + prefix + "-"
                + UUID.randomUUID().toString().substring(0, 8).toLowerCase(Locale.ROOT);
        taskNames.add(name);
        return TaskName.of(name);
    }

    private ExecutionInterceptor recordingPicks() {
        return (taskInstance, context, chain) -> {
            var execution = context.getExecution();
            pickedBy.computeIfAbsent(taskInstance.getId(), id -> new CopyOnWriteArrayList<>())
                    .add(execution.pickedBy);
            ranExecutionTimes
                    .computeIfAbsent(taskInstance.getTaskName(), name -> new CopyOnWriteArrayList<>())
                    .add(execution.executionTime);
            return chain.proceed(taskInstance, context);
        };
    }

    // Built like SchedulingConfiguration builds the application's scheduler; fast timings for the test.
    @SuppressWarnings({"unchecked", "rawtypes"})
    private Scheduler startInstance(String name, ScheduledTask declared) {
        Task<?> task = ((LibraryTask) declared).libraryTask();
        var builder = Scheduler.create(dataSource, task instanceof OnStartup ? List.of() : List.of(task))
                .tableName("platform.scheduled_tasks")
                .schedulerName(new SchedulerName.Fixed(name))
                .serializer(new Jackson3Serializer())
                .pollingInterval(POLLING)
                .heartbeatInterval(HEARTBEAT)
                .missedHeartbeatsLimit(MISSED_HEARTBEATS)
                .shutdownMaxWait(Duration.ofMillis(200))
                .addExecutionInterceptor(recordingPicks())
                .addExecutionInterceptor(new ObservedTaskExecution(observations));
        if (task instanceof OnStartup) {
            builder.startTasks((List) List.of(task));
        }
        var scheduler = builder.build();
        scheduler.start();
        started.add(scheduler);
        instanceA.compareAndSet(null, scheduler);
        return scheduler;
    }

    private static void sleep(Duration duration) {
        try {
            Thread.sleep(duration);
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
        }
    }
}
