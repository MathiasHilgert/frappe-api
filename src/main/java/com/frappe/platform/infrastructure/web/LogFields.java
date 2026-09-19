package com.frappe.platform.infrastructure.web;

/** Names of the structured log fields (SLF4J key/values) this package emits: ECS names for HTTP values. */
final class LogFields {

    /** The request's HTTP method (ECS {@code http.request.method}). */
    static final String HTTP_METHOD = "http.request.method";

    /** The request's path (ECS {@code url.path}). */
    static final String URL_PATH = "url.path";

    private LogFields() {}
}
