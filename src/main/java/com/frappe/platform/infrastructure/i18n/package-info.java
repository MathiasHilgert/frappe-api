/**
 * Locale resolution for HTTP requests: the chain from the user's preference through {@code Accept-Language} and the
 * tenant's defaults to English, the headers that announce the chosen language, and the wiring of the ICU message
 * source.
 */
package com.frappe.platform.infrastructure.i18n;
