package com.frappe.platform.infrastructure.web;

import com.frappe.platform.web.Posture;
import org.springframework.web.servlet.mvc.method.RequestMappingInfo;

/**
 * One application route: its request mapping, the class declaring it and its access posture.
 *
 * @param mapping the request mapping, including the {@code /v1} prefix
 * @param type the route class
 * @param posture who may call the route
 * @param permission the permission {@link Posture#PERMISSION} checks; empty for any other posture
 */
record Route(RequestMappingInfo mapping, Class<?> type, Posture posture, String permission) {}
