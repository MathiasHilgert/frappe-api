package com.frappe.platform.infrastructure.i18n;

import static java.util.stream.Collectors.joining;

import java.util.List;

/**
 * The message catalogs are incomplete or contain invalid messages; thrown at startup so that no request is ever served
 * with them. The message lists every violation, one per line.
 */
final class InvalidMessageCatalogsException extends RuntimeException {

    /**
     * Creates the exception.
     *
     * @param violations every violation found, at least one
     */
    InvalidMessageCatalogsException(List<CatalogViolation> violations) {
        super(violations.stream()
                .map(violation -> "  - " + violation.describe())
                .collect(joining(
                        System.lineSeparator(),
                        "Message catalogs (src/main/resources/i18n/<module>/messages_{en,es,pt}.properties) are invalid;"
                                + " fix each of these:" + System.lineSeparator(),
                        "")));
    }
}
