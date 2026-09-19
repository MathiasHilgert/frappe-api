package com.frappe.platform.infrastructure.web;

import com.frappe.platform.i18n.Messages;
import com.frappe.platform.web.Access;
import com.frappe.platform.web.Posture;
import org.springframework.boot.test.context.TestConfiguration;
import org.springframework.context.i18n.LocaleContextHolder;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.RequestParam;
import org.springframework.web.bind.annotation.RestController;

/** Public routes for tests of locale resolution and localized messages; importing this registers them. */
@TestConfiguration(proxyBeanMethods = false)
public class LocaleProbeRoutes {

    /** Answers the locale the request resolved to, without the {@code /v1} prefix. */
    public static final String LOCALE_PATH = "/test/locale-probe";

    /** Always fails with a server error, without the {@code /v1} prefix. */
    public static final String FAILING_PATH = "/test/locale-probe/failure";

    /** Answers the localized {@code sample.items} message, without the {@code /v1} prefix. */
    public static final String ITEMS_PATH = "/test/locale-probe/items";

    /** Answers the resolved locale. */
    @RestController
    @Access(Posture.PUBLIC)
    public static class LocaleRoute {

        /**
         * Returns the locale Spring MVC resolved for the request.
         *
         * @return its BCP 47 tag
         */
        @GetMapping(LOCALE_PATH)
        public String locale() {
            return LocaleContextHolder.getLocale().toLanguageTag();
        }
    }

    /** Fails, to prove error responses carry the language headers. */
    @RestController
    @Access(Posture.PUBLIC)
    public static class FailingRoute {

        /**
         * Always throws.
         *
         * @return never
         */
        @GetMapping(FAILING_PATH)
        public String failure() {
            throw new IllegalStateException("Probe failure");
        }
    }

    /** Resolves a message through the {@link Messages} port. */
    @RestController
    @Access(Posture.PUBLIC)
    public static class ItemsRoute {

        private final Messages messages;

        ItemsRoute(Messages messages) {
            this.messages = messages;
        }

        /**
         * Returns the localized item count.
         *
         * @param count the number of items
         * @return {@code sample.items} in the request's locale
         */
        @GetMapping(ITEMS_PATH)
        public String items(@RequestParam int count) {
            return messages.get("sample.items", count);
        }
    }
}
