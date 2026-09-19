package com.frappe.platform.infrastructure.i18n;

import io.micrometer.observation.Observation;
import io.micrometer.observation.ObservationRegistry;

/**
 * The observation around one machine-translation call: a span and the {@code translation.request} timer. Tags are
 * low cardinality (the request's bare target language, e.g. {@code pt}, and the outcome); the texts and any glossary
 * reference never appear on the span or the meter.
 *
 * <p>{@link #of} pre-tags the outcome {@link #ERROR}: the caller overwrites it on every path it handles
 * ({@link #SUCCESS}, {@link #UNAVAILABLE}, {@link #INVALID_KEY}, {@link #QUOTA_EXCEEDED}), so an SDK exception the
 * adapter does not explicitly map still reaches the meter as {@link #ERROR} through {@link Observation#observe},
 * without a {@code catch (RuntimeException)}.
 */
final class TranslationObservation {

    /** Observation name; the timer is exported as {@code translation.request}. */
    static final String NAME = "translation.request";

    /** Low-cardinality key: the request's bare target language (e.g. {@code pt}). */
    static final String TARGET_LANGUAGE_TAG = "target_language";

    /** Low-cardinality key: what happened. */
    static final String OUTCOME_TAG = "outcome";

    /** Outcome tag: the call succeeded. */
    static final String SUCCESS = "success";

    /** Outcome tag: no key configured, or the provider could not be reached in time. */
    static final String UNAVAILABLE = "unavailable";

    /** Outcome tag: the provider refused the configured key. */
    static final String INVALID_KEY = "invalid_key";

    /** Outcome tag: the provider's translation quota is exhausted. */
    static final String QUOTA_EXCEEDED = "quota_exceeded";

    /** Outcome tag: an exception the adapter does not explicitly map. */
    static final String ERROR = "error";

    private TranslationObservation() {}

    /**
     * Creates the observation for one translation call, pre-tagged {@link #ERROR}; the caller overwrites the outcome
     * on every path it handles and runs the call through {@link Observation#observe}.
     *
     * @param registry where the observation is recorded
     * @param targetLanguageTag the request's bare target language
     * @return the not yet started observation
     */
    static Observation of(ObservationRegistry registry, String targetLanguageTag) {
        return Observation.createNotStarted(NAME, registry)
                .contextualName("translate to " + targetLanguageTag)
                .lowCardinalityKeyValue(TARGET_LANGUAGE_TAG, targetLanguageTag)
                .lowCardinalityKeyValue(OUTCOME_TAG, ERROR);
    }
}
