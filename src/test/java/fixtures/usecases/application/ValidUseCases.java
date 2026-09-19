package fixtures.usecases.application;

import com.frappe.platform.CommandUseCase;
import com.frappe.platform.QueryUseCase;
import com.frappe.platform.Result;
import org.springframework.transaction.annotation.Propagation;
import org.springframework.transaction.annotation.Transactional;

/** Use cases that follow every rule. */
public final class ValidUseCases {

    private ValidUseCases() {}

    @CommandUseCase
    public static class OpenTab {

        @Transactional
        public Result<String, String> open(String table) {
            return Result.success(table);
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
}
