package fixtures.tabs.infrastructure.web;

import com.frappe.platform.CommandUseCase;
import org.springframework.transaction.annotation.Transactional;

/** A use case outside an {@code application} package. */
@CommandUseCase
public class MisplacedUseCase {

    @Transactional
    public String open(String table) {
        return table;
    }
}
