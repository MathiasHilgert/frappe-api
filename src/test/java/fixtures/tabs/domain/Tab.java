package fixtures.tabs.domain;

import com.frappe.platform.Result;
import java.util.List;

/** A domain type that depends only on the JDK, the kernel and its own domain. */
public record Tab(String table, List<TabLine> lines) {

    public Result<Tab, String> close() {
        return lines.isEmpty() ? Result.failure("empty") : Result.success(this);
    }

    public record TabLine(String item) {}
}
