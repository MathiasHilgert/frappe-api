package fixtures.tabs.application;

import com.frappe.platform.CommandUseCase;
import java.lang.annotation.ElementType;
import java.lang.annotation.Retention;
import java.lang.annotation.RetentionPolicy;
import java.lang.annotation.Target;

/** A composed stereotype: the platform treats meta-annotated classes as use cases too. */
@CommandUseCase
@Retention(RetentionPolicy.RUNTIME)
@Target(ElementType.TYPE)
public @interface TabCommand {}
