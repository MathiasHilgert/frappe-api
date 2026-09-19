package fixtures.tabs.application;

import com.frappe.platform.CommandUseCase;
import com.frappe.platform.QueryUseCase;
import com.frappe.platform.Result;
import fixtures.orders.OrdersApi;
import fixtures.tabs.domain.Tab;
import org.springframework.transaction.annotation.Isolation;
import org.springframework.transaction.annotation.Propagation;
import org.springframework.transaction.annotation.Transactional;

/** Use cases that follow every rule. */
public final class ValidUseCases {

    private ValidUseCases() {}

    @CommandUseCase
    public static class OpenTab {

        private final OrdersApi orders;

        public OpenTab(OrdersApi orders) {
            this.orders = orders;
        }

        @Transactional(isolation = Isolation.REPEATABLE_READ)
        public Result<Tab, String> open(Tab tab) {
            return orders.hasOpenOrders(tab.table()) ? Result.failure("busy") : tab.close();
        }
    }

    @CommandUseCase
    public static class RecordFailedLogin {

        @Transactional(propagation = Propagation.REQUIRES_NEW)
        public void record(String user) {}
    }

    @QueryUseCase
    @Transactional(readOnly = true)
    public static class FindTab {

        public String find(String table) {
            return table;
        }
    }

    @TabCommand
    public static class SplitTab {

        @Transactional
        public String split(String table) {
            return table;
        }
    }
}
