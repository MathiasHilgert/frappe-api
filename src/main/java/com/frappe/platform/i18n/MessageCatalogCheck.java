package com.frappe.platform.i18n;

import static java.util.stream.Collectors.groupingBy;
import static java.util.stream.Collectors.joining;

import com.ibm.icu.text.MessageFormat;
import com.ibm.icu.util.ULocale;
import java.util.ArrayList;
import java.util.Collection;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.TreeMap;
import java.util.TreeSet;

/**
 * Checks message catalogs before they are used: each module has a catalog per supported locale with the same keys,
 * every key lives in the module's namespace, and every message is valid ICU MessageFormat with numbered arguments. A
 * missing key would produce mixed-language output; a broken message would fail at the moment a user needs it.
 */
final class MessageCatalogCheck {

    private MessageCatalogCheck() {}

    /**
     * Returns every violation in the given catalogs, ordered by module, locale and key.
     *
     * @param catalogs the catalogs to check
     * @return the violations; empty when the catalogs are complete and valid
     */
    static List<CatalogViolation> violations(Collection<MessageCatalog> catalogs) {
        var violations = new ArrayList<CatalogViolation>();
        var supported = SupportedLocales.all().locales();
        catalogs.stream()
                .filter(catalog -> !supported.contains(catalog.locale()))
                .forEach(catalog -> violations.add(unsupportedLocale(catalog)));
        catalogs.stream()
                .filter(catalog -> supported.contains(catalog.locale()))
                .collect(groupingBy(MessageCatalog::module, TreeMap::new, groupingBy(MessageCatalog::locale)))
                .forEach((module, byLocale) -> violations.addAll(moduleViolations(module, byLocale)));
        return List.copyOf(violations);
    }

    private static List<CatalogViolation> moduleViolations(String module, Map<Locale, List<MessageCatalog>> byLocale) {
        var violations = new ArrayList<CatalogViolation>();
        var keys = new TreeSet<String>();
        byLocale.values()
                .forEach(sameLocale -> sameLocale.forEach(
                        catalog -> keys.addAll(catalog.messages().keySet())));
        for (var locale : SupportedLocales.all().locales()) {
            var catalogs = byLocale.getOrDefault(locale, List.of());
            if (catalogs.size() > 1) {
                violations.add(CatalogViolation.ofCatalog(
                        module, locale, "exists %d times on the classpath; keep one".formatted(catalogs.size())));
                continue;
            }
            var messages = catalogs.isEmpty()
                    ? Map.<String, String>of()
                    : catalogs.getFirst().messages();
            for (var key : keys) {
                var pattern = messages.get(key);
                if (pattern == null) {
                    violations.add(missingKey(module, locale, key, byLocale));
                } else {
                    violations.addAll(messageViolations(module, locale, key, pattern));
                }
            }
        }
        return violations;
    }

    private static List<CatalogViolation> messageViolations(String module, Locale locale, String key, String pattern) {
        var violations = new ArrayList<CatalogViolation>();
        var namespace = module + ".";
        if (!key.startsWith(namespace)) {
            violations.add(CatalogViolation.ofMessage(
                    module, locale, key, "must start with '%s', the module's namespace".formatted(namespace)));
        }
        try {
            if (new MessageFormat(pattern, ULocale.forLocale(locale)).usesNamedArguments()) {
                violations.add(CatalogViolation.ofMessage(
                        module,
                        locale,
                        key,
                        "uses named arguments; use numbered ones ({0}), which MessageSource passes"));
            }
        } catch (IllegalArgumentException invalid) {
            violations.add(CatalogViolation.ofMessage(
                    module, locale, key, "is not valid ICU MessageFormat syntax: " + invalid.getMessage()));
        }
        return violations;
    }

    private static CatalogViolation missingKey(
            String module, Locale locale, String key, Map<Locale, List<MessageCatalog>> byLocale) {
        var presentIn = SupportedLocales.all().locales().stream()
                .filter(other -> byLocale.getOrDefault(other, List.of()).stream()
                        .anyMatch(catalog -> catalog.messages().containsKey(key)))
                .map(Locale::toLanguageTag)
                .collect(joining(", "));
        return CatalogViolation.ofMessage(
                module,
                locale,
                key,
                "is missing (present in %s); every catalog of a module has the same keys".formatted(presentIn));
    }

    private static CatalogViolation unsupportedLocale(MessageCatalog catalog) {
        var supported = SupportedLocales.all().locales().stream()
                .map(Locale::toLanguageTag)
                .collect(joining(", "));
        return CatalogViolation.ofCatalog(
                catalog.module(),
                catalog.locale(),
                "locale '%s' is not supported (%s)".formatted(catalog.locale().toLanguageTag(), supported));
    }
}
