package com.frappe.platform.web;

import java.util.Collections;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Objects;
import java.util.SequencedMap;
import java.util.regex.Pattern;

/**
 * What a client receives for a business failure, as a {@link ProblemMapper} states it: the HTTP status, the slug of the
 * stable problem type ({@code https://frappe.app/problems/<slug>}, also the problem's {@code code}), the catalog key of
 * its localized text and its params. The platform answers it as an RFC 9457 problem with the title
 * {@code <messageKey>.title} and the detail {@code <messageKey>.detail} of the module's catalogs, in the request's
 * locale; the params fill the detail's numbered placeholders in order and are returned raw as {@code params}.
 *
 * {@snippet :
 * Problem.of(409, "tab-already-closed", "order.tab.already-closed").with("tabId", tabId);
 * }
 *
 * <p>Clients branch on the type or code, never on the text. Params are raw values (ids, counts, codes), never
 * formatted text, personal data beyond what the caller sent, or internals.
 *
 * @param status the HTTP status, a client error (4xx): a business failure is never a server fault
 * @param slug the problem type's slug, kebab-case and stable once published
 * @param messageKey the catalog key the title and detail keys start with, such as {@code order.tab.already-closed}
 * @param params the named values of the problem, in the order of the detail's placeholders
 */
public record Problem(int status, String slug, String messageKey, SequencedMap<String, Object> params) {

    private static final Pattern KEBAB_CASE = Pattern.compile("[a-z0-9]+(-[a-z0-9]+)*");

    /**
     * Validates the problem and keeps an unmodifiable copy of its params.
     *
     * @param status the HTTP status, 400 to 499
     * @param slug the problem type's slug, kebab-case
     * @param messageKey the catalog key, not blank
     * @param params the named values, not {@code null}
     * @throws IllegalArgumentException for a status outside 4xx, a slug that is not kebab-case or a blank key
     */
    public Problem {
        if (status < 400 || status > 499) {
            throw new IllegalArgumentException("Status " + status + " is no client error; a business failure is 4xx");
        }
        Objects.requireNonNull(slug, "slug");
        if (!KEBAB_CASE.matcher(slug).matches()) {
            throw new IllegalArgumentException("Slug '" + slug + "' must be kebab-case, like 'tab-already-closed'");
        }
        Objects.requireNonNull(messageKey, "messageKey");
        if (messageKey.isBlank()) {
            throw new IllegalArgumentException("The message key must not be blank");
        }
        params = Collections.unmodifiableSequencedMap(new LinkedHashMap<>(params));
    }

    /**
     * Creates a problem without params.
     *
     * @param status the HTTP status, 400 to 499
     * @param slug the problem type's slug, kebab-case
     * @param messageKey the catalog key of the title and detail
     * @return the problem
     */
    public static Problem of(int status, String slug, String messageKey) {
        return new Problem(status, slug, messageKey, new LinkedHashMap<>());
    }

    /**
     * Returns this problem with one more param, placed after the existing ones.
     *
     * @param name the param's name, as clients read it
     * @param value its raw value, not {@code null}
     * @return the extended problem
     */
    public Problem with(String name, Object value) {
        Objects.requireNonNull(name, "name");
        if (name.isBlank()) {
            throw new IllegalArgumentException("A param name must not be blank");
        }
        Objects.requireNonNull(value, "value of param " + name);
        var extended = new LinkedHashMap<>(params);
        extended.put(name, value);
        return new Problem(status, slug, messageKey, extended);
    }

    /**
     * The values of the params in order: the arguments of the detail message ({@code {0}}, {@code {1}}, …).
     *
     * @return the param values
     */
    public List<Object> arguments() {
        return List.copyOf(params.sequencedValues());
    }
}
