package com.frappe.platform.infrastructure.web;

import io.micrometer.observation.Observation;
import io.micrometer.observation.ObservationPredicate;

/**
 * Drops Spring Security's filter-chain observations ({@code spring.security.filterchains}: a "before" and an "after"
 * span and timer on every request) that only measure the security plumbing. The authorization, authentication and
 * secured-request observations stay: they carry the decision and its latency.
 */
final class SecurityFilterChainObservations implements ObservationPredicate {

    /** Name prefix of the dropped observations. */
    static final String FILTER_CHAINS = "spring.security.filterchains";

    /** Creates the predicate. */
    SecurityFilterChainObservations() {}

    @Override
    public boolean test(String name, Observation.Context context) {
        return !name.startsWith(FILTER_CHAINS);
    }
}
