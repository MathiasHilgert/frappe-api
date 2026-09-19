package fixtures.tabs.domain;

import fixtures.orders.domain.Order;
import fixtures.tabs.application.ValidUseCases;
import org.springframework.util.Assert;

/** Domain types that each break the allow-list. */
public final class InvalidDomain {

    private InvalidDomain() {}

    public record SpringAwareTab(String table) {

        public SpringAwareTab {
            Assert.hasText(table, "table must not be empty");
        }
    }

    public record TabOfOrder(Order order) {}

    public record TabCallingItsUseCase(ValidUseCases.OpenTab openTab) {}
}
