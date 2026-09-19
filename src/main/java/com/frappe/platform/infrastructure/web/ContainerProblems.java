package com.frappe.platform.infrastructure.web;

import java.util.LinkedHashMap;
import java.util.Locale;
import java.util.Map;
import org.springframework.context.MessageSource;
import org.springframework.http.HttpStatusCode;
import tools.jackson.databind.json.JsonMapper;

/**
 * The problem bodies for requests the servlet container refuses before any filter or Spring runs (a garbled request
 * line, an invalid path or header, TRACE): the platform problem for the status, in English (no locale was resolved),
 * without a trace id (no trace was started). Same shape and text as everywhere else; nothing of the refusal's cause.
 */
final class ContainerProblems {

    private final MessageSource messages;
    private final JsonMapper json;

    /**
     * Creates the renderer.
     *
     * @param messages the application message source
     * @param json the application's JSON mapper
     */
    ContainerProblems(MessageSource messages, JsonMapper json) {
        this.messages = messages;
        this.json = json;
    }

    /**
     * The problem JSON for a status.
     *
     * @param status the status the container answers with
     * @return the {@code application/problem+json} body
     */
    String bodyFor(int status) {
        var type = ProblemType.of(HttpStatusCode.valueOf(status));
        var body = new LinkedHashMap<String, Object>();
        body.put("type", Problems.TYPE_BASE + type.slug());
        body.put("title", messages.getMessage(type.messageKey() + ".title", null, Locale.ENGLISH));
        body.put("status", status);
        body.put("detail", messages.getMessage(type.messageKey() + ".detail", null, Locale.ENGLISH));
        body.put("code", type.slug());
        body.put("params", Map.of());
        return json.writeValueAsString(body);
    }
}
