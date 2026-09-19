package fixtures.tabs.application;

import com.frappe.platform.CommandUseCase;
import com.frappe.platform.QueryUseCase;
import fixtures.orders.domain.Order;
import fixtures.tabs.infrastructure.persistence.TabEntity;
import io.micrometer.observation.ObservationRegistry;
import org.springframework.transaction.annotation.Propagation;
import org.springframework.transaction.annotation.Transactional;
import org.springframework.util.Assert;

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

    @TabCommand
    public static class MetaAnnotatedTwoOperations {

        @Transactional
        public String open(String table) {
            return table;
        }

        @Transactional
        public String close(String table) {
            return table;
        }
    }

    public static class OperationBase {

        @Transactional
        public String audit(String table) {
            return table;
        }
    }

    @CommandUseCase
    public static class InheritsASecondOperation extends OperationBase {

        @Transactional
        public String open(String table) {
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
    public static final class FinalUseCase {

        @Transactional
        public String open(String table) {
            return table;
        }
    }

    @CommandUseCase
    public static class FinalOperation {

        @Transactional
        public final String open(String table) {
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

    @CommandUseCase
    public static class NestedCommand {

        @Transactional(propagation = Propagation.NESTED)
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

    @CommandUseCase
    public static class UsesSpringUtility {

        @Transactional
        public String open(String table) {
            Assert.hasText(table, "table");
            return table;
        }
    }

    @CommandUseCase
    public static class UsesOwnInfrastructure {

        @Transactional
        public String open(TabEntity entity) {
            return entity.toString();
        }
    }

    @CommandUseCase
    public static class UsesOtherModuleDomain {

        @Transactional
        public String open(Order order) {
            return order.id();
        }
    }
}
