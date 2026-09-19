package com.frappe.platform.i18n;

import java.util.Locale;
import java.util.Objects;
import java.util.Optional;
import java.util.Set;

/**
 * Locale settings of one business, as seen by one request.
 *
 * <p>The enabled languages limit what the locale chain may choose; a language outside them (or outside
 * {@link SupportedLocales}) is skipped, and the chain moves on to its next link.
 *
 * @param enabledLanguages the languages the business offers
 * @param businessDefault the business's default language, used when neither the user nor the client names an enabled
 *     one and the branch has no default
 * @param branchDefault the default language of the branch the request is about, if the branch sets one
 */
public record TenantLocales(Set<Locale> enabledLanguages, Locale businessDefault, Optional<Locale> branchDefault) {

    /**
     * Validates presence and copies the enabled languages.
     *
     * @param enabledLanguages the languages the business offers
     * @param businessDefault the business's default language
     * @param branchDefault the branch's default language, if any
     */
    public TenantLocales {
        enabledLanguages = Set.copyOf(enabledLanguages);
        Objects.requireNonNull(businessDefault, "businessDefault");
        Objects.requireNonNull(branchDefault, "branchDefault");
    }

    /**
     * Creates the settings of a business, for a request without a branch default.
     *
     * @param enabledLanguages the languages the business offers
     * @param businessDefault the business's default language
     * @return the settings
     */
    public static TenantLocales of(Set<Locale> enabledLanguages, Locale businessDefault) {
        return new TenantLocales(enabledLanguages, businessDefault, Optional.empty());
    }

    /**
     * Returns these settings for a branch that has its own default language.
     *
     * @param branchDefault the branch's default language
     * @return a copy with the branch default set
     */
    public TenantLocales withBranchDefault(Locale branchDefault) {
        return new TenantLocales(enabledLanguages, businessDefault, Optional.of(branchDefault));
    }
}
