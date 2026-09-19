package fixtures.usecases.application;

import com.frappe.platform.CommandUseCase;
import com.frappe.platform.QueryUseCase;
import io.micrometer.observation.ObservationRegistry;
import org.springframework.transaction.annotation.Propagation;
import org.springframework.transaction.annotation.Transactional;

/** Use cases that each break one rule; they live outside {@code com.frappe}, so production checks never see them. */
public final class InvalidUseCases {

    private InvalidUseCases() {}

    @CommandUseCase
    public static class TwoOperations {

        @Transactional
        public String open(String table) {
            return table;
        }

        @Transactional
        public String close(String table) {
            return table;
        }
    }

    @CommandUseCase
    @QueryUseCase
    public static class BothKinds {

        @Transactional
        public String run(String table) {
            return table;
        }
    }

    @CommandUseCase
    public static class NonTransactionalCommand {

        public String open(String table) {
            return table;
        }
    }

    @CommandUseCase
    public static class ReadOnlyCommand {

        @Transactional(readOnly = true)
        public String open(String table) {
            return table;
        }
    }

    @QueryUseCase
    public static class ReadWriteQuery {

        @Transactional
        public String find(String table) {
            return table;
        }
    }

    @QueryUseCase
    public static class SupportsQuery {

        @Transactional(readOnly = true, propagation = Propagation.SUPPORTS)
        public String find(String table) {
            return table;
        }
    }

    @CommandUseCase
    public static class ObservingCommand {

        private final ObservationRegistry observations;

        public ObservingCommand(ObservationRegistry observations) {
            this.observations = observations;
        }

        @Transactional
        public String open(String table) {
            return table + observations;
        }
    }
}
