package com.frappe.platform.infrastructure.web;

import com.frappe.platform.web.Problem;
import io.micrometer.tracing.Tracer;
import jakarta.servlet.http.HttpServletRequest;
import jakarta.validation.ConstraintViolation;
import java.net.URI;
import java.util.Comparator;
import java.util.Locale;
import java.util.Map;
import java.util.Optional;
import java.util.Set;
import java.util.TreeMap;
import java.util.regex.Pattern;
import org.springframework.context.MessageSource;
import org.springframework.context.support.DefaultMessageSourceResolvable;
import org.springframework.http.HttpStatusCode;
import org.springframework.http.ProblemDetail;
import org.springframework.validation.BindingResult;
import org.springframework.validation.FieldError;
import org.springframework.validation.ObjectError;
import org.springframework.web.servlet.LocaleResolver;
import tools.jackson.core.JsonPointer;

/**
 * Builds every problem the API answers: RFC 9457 {@link ProblemDetail}s with a stable {@code type} URI, {@code code}
 * and {@code params}, the {@code traceId} of the request, and a title and detail from the catalogs in the locale the
 * request resolved to. Text never comes from an exception.
 */
final class Problems {

    /** Every problem type URI starts with this; the slug follows. */
    static final String TYPE_BASE = "https://frappe.app/problems/";

    private static final String VALIDATION_KEYS = "platform.validation.";
    private static final String INVALID_FIELD = VALIDATION_KEYS + "invalid";
    /**
     * The constraint attributes a client can act on (bounds, digits); everything else (patterns, flags, groups,
     * payload and validator classes) describes our code, not the client's input, and is never returned.
     */
    private static final Set<String> CLIENT_ATTRIBUTES =
            Set.of("min", "max", "value", "inclusive", "integer", "fraction");

    private static final Comparator<ValidationError> BY_POINTER_THEN_CODE =
            Comparator.comparing(ValidationError::pointer).thenComparing(ValidationError::code);
    private static final Pattern WORD_BOUNDARY = Pattern.compile("([a-z0-9])([A-Z])");
    private static final Pattern INDEX_OR_KEY = Pattern.compile("\\[([^]]*)]");

    private final MessageSource messages;
    private final LocaleResolver locales;
    private final Tracer tracer;

    /**
     * Creates the factory.
     *
     * @param messages the application message source over the catalogs
     * @param locales the locale chain of the request
     * @param tracer the tracer, for the trace id of the current request
     */
    Problems(MessageSource messages, LocaleResolver locales, Tracer tracer) {
        this.messages = messages;
        this.locales = locales;
        this.tracer = tracer;
    }

    /**
     * The platform problem for a status the framework chose.
     *
     * @param status the status
     * @param request the current request
     * @return the problem
     */
    ProblemDetail forStatus(HttpStatusCode status, HttpServletRequest request) {
        var type = ProblemType.of(status);
        var answered = type == ProblemType.REQUEST_REJECTED ? status.value() : type.status();
        return problem(answered, type.slug(), type.messageKey(), new Object[0], Map.of(), request);
    }

    /**
     * The invalid-request problem for a body that fails validation: one {@code errors} entry per violated field, with
     * its JSON pointer, the constraint as code, the constraint's attributes as params and a localized detail.
     *
     * @param result the validation result of the body
     * @param request the current request
     * @return the problem
     */
    ProblemDetail invalidFields(BindingResult result, HttpServletRequest request) {
        var type = ProblemType.INVALID_REQUEST;
        var locale = locales.resolveLocale(request);
        var errors = result.getAllErrors().stream()
                .map(error -> fieldError(error, locale))
                .sorted(BY_POINTER_THEN_CODE)
                .toList();
        var body = problem(
                type.status(),
                type.slug(),
                type.messageKey(),
                "fields",
                new Object[] {errors.size()},
                Map.of(),
                request);
        body.setProperty("errors", errors);
        return body;
    }

    /**
     * The problem a module mapped a business failure to.
     *
     * @param mapped the mapped problem
     * @param request the current request
     * @return the problem
     */
    ProblemDetail mapped(Problem mapped, HttpServletRequest request) {
        return problem(
                mapped.status(),
                mapped.slug(),
                mapped.messageKey(),
                mapped.arguments().toArray(),
                mapped.params(),
                request);
    }

    private ProblemDetail problem(
            int status,
            String slug,
            String messageKey,
            Object[] arguments,
            Map<String, Object> params,
            HttpServletRequest request) {
        return problem(status, slug, messageKey, "detail", arguments, params, request);
    }

    private ProblemDetail problem(
            int status,
            String slug,
            String messageKey,
            String detailSuffix,
            Object[] arguments,
            Map<String, Object> params,
            HttpServletRequest request) {
        var locale = locales.resolveLocale(request);
        var body = ProblemDetail.forStatus(status);
        body.setType(URI.create(TYPE_BASE + slug));
        body.setTitle(messages.getMessage(messageKey + ".title", null, locale));
        body.setDetail(messages.getMessage(messageKey + "." + detailSuffix, arguments, locale));
        body.setProperty("code", slug);
        body.setProperty("params", params);
        traceId().ifPresent(traceId -> body.setProperty("traceId", traceId));
        return body;
    }

    private ValidationError fieldError(ObjectError error, Locale locale) {
        var pointer = error instanceof FieldError field ? pointerOf(field.getField()) : "";
        if (error.contains(ConstraintViolation.class)) {
            var constraint = error.unwrap(ConstraintViolation.class).getConstraintDescriptor();
            var code = kebabCase(constraint.getAnnotation().annotationType().getSimpleName());
            // Attributes sorted by name: the detail message's {0}, {1}, … follow this order.
            var params = new TreeMap<String, Object>(constraint.getAttributes());
            params.entrySet()
                    .removeIf(attribute ->
                            !CLIENT_ATTRIBUTES.contains(attribute.getKey()) || !isScalar(attribute.getValue()));
            return new ValidationError(
                    pointer, code, params, detailOf(code, params.values().toArray(), locale));
        }
        var code = kebabCase(String.valueOf(error.getCode()));
        return new ValidationError(pointer, code, Map.of(), detailOf(code, new Object[0], locale));
    }

    private static boolean isScalar(Object value) {
        return value instanceof Number || value instanceof Boolean || value instanceof String;
    }

    private String detailOf(String code, Object[] arguments, Locale locale) {
        var resolvable =
                new DefaultMessageSourceResolvable(new String[] {VALIDATION_KEYS + code, INVALID_FIELD}, arguments);
        return messages.getMessage(resolvable, locale);
    }

    private Optional<String> traceId() {
        return Optional.ofNullable(tracer.currentTraceContext().context()).map(context -> context.traceId());
    }

    // Spring's property path (lines[0].quantity, attributes[key]) as an RFC 6901 JSON pointer (/lines/0/quantity).
    private static String pointerOf(String field) {
        var pointer = JsonPointer.empty();
        for (var segment : INDEX_OR_KEY.matcher(field).replaceAll(".$1").split("\\.")) {
            if (!segment.isEmpty()) {
                pointer = pointer.appendProperty(segment);
            }
        }
        return pointer.toString();
    }

    private static String kebabCase(String name) {
        return WORD_BOUNDARY.matcher(name).replaceAll("$1-$2").toLowerCase(Locale.ROOT);
    }

    /**
     * One violated field of a request body.
     *
     * @param pointer the JSON pointer of the field in the body ({@code ""} for the body as a whole)
     * @param code the violated constraint, kebab-case ({@code not-blank}, {@code size})
     * @param params the constraint's attributes, raw ({@code {"max": 5, "min": 2}})
     * @param detail what is wrong, localized
     */
    record ValidationError(String pointer, String code, Map<String, Object> params, String detail) {}
}
