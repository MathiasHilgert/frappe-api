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

        private final Clock clock;

        public Compliant(Clock clock) {
            this.clock = clock;
        }

        Instant now() {
            return Instant.now(clock);
        }

        String name() {
            return NAME;
        }
    }
}
