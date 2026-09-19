package com.frappe.platform.infrastructure.web;

import java.util.Arrays;
import org.springframework.http.HttpStatusCode;

/**
 * The platform's own problem types, for failures no module maps: what the framework, the security chain and unexpected
 * exceptions answer with. The type follows the HTTP status, so every framework exception with the same status (Spring
 * has several per status) shares one stable type, code and text.
 */
enum ProblemType {

    /** 400: unreadable body, bad parameters, or fields that fail validation. */
    INVALID_REQUEST(400, "invalid-request"),
    /** 401: no session, or a token that resolves to none. */
    UNAUTHENTICATED(401, "unauthenticated"),
    /** 403: a caller with a session asked for a path the chain refuses (fail closed). */
    FORBIDDEN(403, "forbidden"),
    /** 404: no route serves the path. */
    NOT_FOUND(404, "not-found"),
    /** 405: a route serves the path, but not with this method ({@code Allow} lists the methods). */
    METHOD_NOT_ALLOWED(405, "method-not-allowed"),
    /** 406: no representation the client accepts. */
    NOT_ACCEPTABLE(406, "not-acceptable"),
    /** 413: the body exceeds the allowed size. */
    CONTENT_TOO_LARGE(413, "content-too-large"),
    /** 415: the body's media type is not supported. */
    UNSUPPORTED_MEDIA_TYPE(415, "unsupported-media-type"),
    /** Any other client error the framework raises. */
    REQUEST_REJECTED(400, "request-rejected"),
    /** 500: anything unexpected; never carries its cause. */
    INTERNAL_ERROR(500, "internal-error");

    private final int status;
    private final String slug;

    ProblemType(int status, String slug) {
        this.status = status;
        this.slug = slug;
    }

    /**
     * The platform type for a status: its own type for the statuses above, {@link #REQUEST_REJECTED} for another client
     * error, {@link #INTERNAL_ERROR} for every server error.
     *
     * @param status the status the framework chose
     * @return the problem type
     */
    static ProblemType of(HttpStatusCode status) {
        if (status.is5xxServerError()) {
            return INTERNAL_ERROR;
        }
        return Arrays.stream(values())
                .filter(type -> type != REQUEST_REJECTED && type.status == status.value())
                .findFirst()
                .orElse(REQUEST_REJECTED);
    }

    /**
     * The HTTP status of this type; {@link #REQUEST_REJECTED} keeps the status the framework chose.
     *
     * @return the status
     */
    int status() {
        return status;
    }

    /**
     * The slug: last segment of the type URI and the problem's {@code code}.
     *
     * @return the slug
     */
    String slug() {
        return slug;
    }

    /**
     * The catalog key the title ({@code .title}) and detail ({@code .detail}) keys start with.
     *
     * @return {@code platform.problem.<slug>}
     */
    String messageKey() {
        return "platform.problem." + slug;
    }
}
