package fixtures.registration.application;

import com.frappe.platform.CommandUseCase;
import com.frappe.platform.QueryUseCase;
import java.lang.annotation.ElementType;
import java.lang.annotation.Retention;
import java.lang.annotation.RetentionPolicy;
import java.lang.annotation.Target;
import org.springframework.transaction.annotation.Transactional;

/** Classes the use case scan must (and must not) register. */
public final class RegisteredUseCases {

    private RegisteredUseCases() {}

    @CommandUseCase
    @Retention(RetentionPolicy.RUNTIME)
    @Target(ElementType.TYPE)
    public @interface RegistrationCommand {}

    @CommandUseCase
    public static class OpenTab {

        @Transactional
        public String open(String table) {
            return table;
        }
    }

    @QueryUseCase
    public static class FindTab {

        @Transactional(readOnly = true)
        public String find(String table) {
            return table;
        }
    }

    @RegistrationCommand
    public static class SplitTab {

        @Transactional
        public String split(String table) {
            return table;
        }
    }

    public static class NotAUseCase {}
}
