package com.frappe.platform.infrastructure.i18n;

import io.micrometer.observation.Observation;
import io.micrometer.observation.ObservationRegistry;

/**
 * The observation around one machine-translation call: a span and the {@code translation.request} timer. Tags are
 * low cardinality (the target language, one of {@code en}/{@code es}/{@code pt}, and the outcome); the texts and any
 * glossary reference never appear on the span or the meter, only in application logs when something fails.
 */
final class TranslationObservation {

    /** Observation name; the timer is exported as {@code translation.request}. */
    static final String NAME = "translation.request";

    /** Low-cardinality key: the request's target language (BCP 47 tag, e.g. {@code pt}). */
    static final String TARGET_LANGUAGE_TAG = "target_language";

    /** Low-cardinality key: what happened. */
    static final String OUTCOME_TAG = "outcome";

    /** Outcome tag: the call succeeded. */
    static final String SUCCESS = "success";

    /** Outcome tag: no key configured, or the provider could not be reached in time. */
    static final String UNAVAILABLE = "unavailable";

    /** Outcome tag: the provider's translation quota is exhausted. */
    static final String QUOTA_EXCEEDED = "quota_exceeded";

    private TranslationObservation() {}

    /**
     * Creates the observation for one translation call; the caller starts it, sets the outcome, and stops it.
     *
     * @param registry where the observation is recorded
     * @param targetLanguageTag the request's target language, as a BCP 47 tag
     * @return the not yet started observation
     */
    static Observation of(ObservationRegistry registry, String targetLanguageTag) {
        return Observation.createNotStarted(NAME, registry)
                .contextualName("translate to " + targetLanguageTag)
                .lowCardinalityKeyValue(TARGET_LANGUAGE_TAG, targetLanguageTag);
    }
}
