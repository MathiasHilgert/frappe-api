/**
 * HTTP route contracts other modules build on: every route declares its {@link com.frappe.platform.web.Access access
 * posture}, and identity implements the port that resolves bearer tokens to sessions. The HTTP layer authenticates
 * only; authorization lives in the application layer.
 */
@NamedInterface("web")
package com.frappe.platform.web;

import org.springframework.modulith.NamedInterface;
