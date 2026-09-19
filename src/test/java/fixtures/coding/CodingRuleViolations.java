package fixtures.coding;

import java.time.Clock;
import java.time.Instant;
import java.util.UUID;
import org.springframework.beans.factory.annotation.Autowired;

/** Classes that each break one coding rule; outside {@code com.frappe}, so production checks never see them. */
public final class CodingRuleViolations {

    private CodingRuleViolations() {}

    public static class FieldInjected {

        @Autowired
        Clock clock;
    }

    public static class StaticMutableState {

        static int counter;
    }

    public static class StaticMutableFinalState {

        static final String[] PATHS = {"/a"};

        static final java.util.List<String> NAMES = new java.util.ArrayList<>();

        static final java.util.concurrent.atomic.AtomicLong COUNTER = new java.util.concurrent.atomic.AtomicLong();

        static final java.util.Map<String, String> CACHE = new java.util.concurrent.ConcurrentHashMap<>();

        static final ClassValue<String> NAMES_BY_CLASS = new ClassValue<>() {
            @Override
            protected String computeValue(Class<?> type) {
                return type.getName();
            }
        };
    }

    public static class ReadsTheLocalDate {

        java.time.LocalDate today() {
            return java.time.LocalDate.now();
        }
    }

    public static class ReadsSystemMillis {

        long millis() {
            return System.currentTimeMillis();
        }
    }

    public static class ClockConfiguration {

        public Clock clock() {
            return Clock.systemUTC();
        }

        public Clock anotherClock() {
            return Clock.systemDefaultZone();
        }
    }

    public static class ReadsTheWallClock {

        Instant now() {
            return Instant.now();
        }
    }

    public static class CreatesRandomIds {

        UUID id() {
            return UUID.randomUUID();
        }
    }

    public static class CreatesItsOwnClock {

        Clock clock() {
            return Clock.systemUTC();
        }
    }

    public static class Compliant {

        private static final String NAME = "compliant";

        private static final java.util.List<String> NAMES = java.util.List.of("a", "b");

        private final Clock clock;

        public Compliant(Clock clock) {
            this.clock = clock;
        }

        Instant now() {
            return Instant.now(clock);
        }

        String name() {
            return NAME + NAMES;
        }

        java.time.LocalDate today() {
            return java.time.LocalDate.now(clock);
        }
    }
}
