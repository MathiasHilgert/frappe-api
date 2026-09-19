package com.frappe.platform.infrastructure.web;

import com.frappe.platform.web.Posture;
import org.springframework.web.servlet.mvc.method.RequestMappingInfo;

/**
 * One application route: its request mapping and its access posture.
 *
 * @param mapping the request mapping, including the {@code /v1} prefix
 * @param posture whether the route needs an authenticated caller
 */
record Route(RequestMappingInfo mapping, Posture posture) {}
