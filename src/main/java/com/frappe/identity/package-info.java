/**
 * Identity: people, their credentials and sessions. Depends only on the platform; every other module calls it. The
 * root package is the public contract: {@code IdentityApi} (later) and the SPIs identity requires from other modules,
 * such as {@link com.frappe.identity.CodeMailer}, which notification implements.
 */
package com.frappe.identity;
