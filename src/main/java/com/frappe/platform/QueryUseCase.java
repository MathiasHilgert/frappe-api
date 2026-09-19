package com.frappe.platform;

import java.lang.annotation.Documented;
import java.lang.annotation.ElementType;
import java.lang.annotation.Retention;
import java.lang.annotation.RetentionPolicy;
import java.lang.annotation.Target;

/**
 * Marks a class as one use case that reads state: a single public method returning a read model, called directly. The
 * platform makes the class a bean and observes every call (span and {@code use_case} timer); the method is
 * {@code @Transactional(readOnly = true)}, so it reads in one transaction that carries the tenant setting row-level
 * security needs.
 *
 * <p>Plain Java on purpose, like jMolecules' stereotypes: use cases depend on the kernel, not on a framework. The
 * architecture tests enforce the shape (package {@code ..application..}, one public method, the transaction of the
 * kind).
 */
@Documented
@Retention(RetentionPolicy.RUNTIME)
@Target(ElementType.TYPE)
public @interface QueryUseCase {}
