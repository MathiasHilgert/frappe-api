/**
 * Identity: people, their credentials and sessions. Depends only on the platform; every other module calls it. The
 * root package is the public contract: {@code IdentityApi} (later) and the SPIs identity requires from other modules,
 * such as {@link com.frappe.identity.CodeMailer}, which notification implements.
 *
 * <p>identity depends only on the platform (its kernel and named interfaces): every other module depends on identity,
 * so any dependency back would be a cycle. Declared here, so {@code ModularityTests} fails on a violation.
 */
@ApplicationModule(allowedDependencies = {"platform", "platform :: *"})
package com.frappe.identity;

import org.springframework.modulith.ApplicationModule;
