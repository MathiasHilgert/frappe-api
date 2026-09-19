package com.frappe.platform;

import java.lang.annotation.Documented;
import java.lang.annotation.ElementType;
import java.lang.annotation.Retention;
import java.lang.annotation.RetentionPolicy;
import java.lang.annotation.Target;

/**
 * Marks a class as one use case that changes state: a single public method, called directly by its callers (a
 * controller, the module's {@code Api}, a listener). The platform makes the class a bean and observes every call (span
 * and {@code use_case} timer); the method is {@code @Transactional} (read-write), and a returned
 * {@link Result.Failure} rolls back the transaction it started.
 *
 * <p>Plain Java on purpose, like jMolecules' stereotypes: use cases depend on the kernel, not on a framework. The
 * architecture tests enforce the shape (package {@code ..application..}, one public method, the transaction of the
 * kind).
 */
@Documented
@Retention(RetentionPolicy.RUNTIME)
@Target(ElementType.TYPE)
public @interface CommandUseCase {}
