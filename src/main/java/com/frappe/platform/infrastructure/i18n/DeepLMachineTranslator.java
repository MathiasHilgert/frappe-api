package com.frappe.platform.infrastructure.i18n;

import com.deepl.api.AuthorizationException;
import com.deepl.api.ConnectionException;
import com.deepl.api.DeepLClient;
import com.deepl.api.DeepLException;
import com.deepl.api.Formality;
import com.deepl.api.QuotaExceededException;
import com.deepl.api.TextResult;
import com.deepl.api.TextTranslationOptions;
import com.deepl.api.TooManyRequestsException;
import com.frappe.platform.i18n.MachineTranslationQuotaExceededException;
import com.frappe.platform.i18n.MachineTranslationUnavailableException;
import com.frappe.platform.i18n.MachineTranslator;
import com.frappe.platform.i18n.SupportedLocales;
import com.frappe.platform.i18n.Translation;
import com.frappe.platform.i18n.TranslationFormality;
import com.frappe.platform.i18n.TranslationRequest;
import io.micrometer.core.instrument.MeterRegistry;
import io.micrometer.observation.Observation;
import io.micrometer.observation.ObservationRegistry;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Optional;

/**
 * {@link MachineTranslator} over the official DeepL Java SDK. One call, one bounded attempt: the client is configured
 * with {@code maxRetries(0)}, because the SDK's own retry loop would silently extend a call past our timeout, and the
 * observation's {@code outcome} tag already tells callers what happened. HTML/tag handling stays off (the SDK
 * default): callers pass plain text, never markup.
 *
 * <p>Without a configured key ({@code client} empty) {@link #isAvailable()} reports {@code false} and
 * {@link #translate} fails fast with {@link MachineTranslationUnavailableException}, without any HTTP call.
 *
 * <p>Provider failures never reach the caller with the provider's name or response: they are mapped here to our own
 * sanitized exceptions, the provider failure preserved only as their {@linkplain Throwable#getCause() cause} — not
 * logged here. Logging happens once, at the boundary that handles the failure (the web layer's catch-all for HTTP
 * callers, FAPI-14; a caller's own boundary otherwise), per {@code errors.md}. Every path is still counted through the
 * {@code translation.request} observation's {@code outcome} tag: an SDK exception this adapter does not explicitly
 * map still surfaces as {@code error} through {@link Observation#observe}, without a {@code catch (RuntimeException)}.
 */
final class DeepLMachineTranslator implements MachineTranslator {

    /** Meter name of the billed-characters counter (DeepL bills per character), incremented on success only. */
    static final String CHARACTERS_METRIC = "translation.characters";

    /** Low-cardinality tag: the request's bare target language. */
    static final String TARGET_LANGUAGE_TAG = "target_language";

    /** DeepL-supported explicit region variants; any other target language is sent bare or with its default variant. */
    private static final List<String> SUPPORTED_VARIANTS = List.of("en-GB", "en-US", "pt-PT", "pt-BR", "es-419");

    private final Optional<DeepLClient> client;

    private final MeterRegistry meters;

    private final ObservationRegistry observations;

    private final Map<String, String> defaultVariants;

    /**
     * Creates the adapter with a configured client.
     *
     * @param client the DeepL client, already pointed at the right server URL and account key
     * @param meters where the characters counter is recorded
     * @param observations where the request observation is recorded
     * @param defaultVariants the region variant to use for a bare target language DeepL requires one for
     */
    DeepLMachineTranslator(
            DeepLClient client,
            MeterRegistry meters,
            ObservationRegistry observations,
            Map<String, String> defaultVariants) {
        this(Optional.of(client), meters, observations, defaultVariants);
    }

    /**
     * Creates the adapter without a key: every call reports unavailable, and the application still starts.
     *
     * @param meters where the characters counter is recorded
     * @param observations where the request observation is recorded
     */
    DeepLMachineTranslator(MeterRegistry meters, ObservationRegistry observations) {
        this(Optional.empty(), meters, observations, Map.of());
    }

    private DeepLMachineTranslator(
            Optional<DeepLClient> client,
            MeterRegistry meters,
            ObservationRegistry observations,
            Map<String, String> defaultVariants) {
        this.client = client;
        this.meters = meters;
        this.observations = observations;
        this.defaultVariants = defaultVariants;
    }

    @Override
    public boolean isAvailable() {
        return client.isPresent();
    }

    @Override
    public List<Translation> translate(TranslationRequest request) {
        var targetTag = request.targetLanguage().getLanguage();
        var observation = TranslationObservation.of(observations, targetTag);
        return observation.observe(() -> execute(request, observation, targetTag));
    }

    private List<Translation> execute(TranslationRequest request, Observation observation, String targetTag) {
        if (client.isEmpty()) {
            observation.lowCardinalityKeyValue(TranslationObservation.OUTCOME_TAG, TranslationObservation.UNAVAILABLE);
            throw noKeyConfigured();
        }
        try {
            var results = translateWith(client.get(), request, deepLTargetLanguage(request.targetLanguage()));
            observation.lowCardinalityKeyValue(TranslationObservation.OUTCOME_TAG, TranslationObservation.SUCCESS);
            recordCharacters(targetTag, results);
            return results.stream().map(DeepLMachineTranslator::toTranslation).toList();
        } catch (MachineTranslationQuotaExceededException e) {
            observation.lowCardinalityKeyValue(
                    TranslationObservation.OUTCOME_TAG, TranslationObservation.QUOTA_EXCEEDED);
            throw e;
        } catch (InvalidKeyException e) {
            observation.lowCardinalityKeyValue(TranslationObservation.OUTCOME_TAG, TranslationObservation.INVALID_KEY);
            throw e.sanitized();
        } catch (MachineTranslationUnavailableException e) {
            observation.lowCardinalityKeyValue(TranslationObservation.OUTCOME_TAG, TranslationObservation.UNAVAILABLE);
            throw e;
        }
    }

    private static List<TextResult> translateWith(
            DeepLClient client, TranslationRequest request, String deepLTargetTag) {
        var options = new TextTranslationOptions().setFormality(formality(request.formality()));
        if (request.glossaryReference() != null) {
            options.setGlossaryId(request.glossaryReference());
        }
        // DeepL only accepts the bare language as a source; only target languages ever need a region variant.
        var sourceTag = request.sourceLanguage() == null
                ? null
                : request.sourceLanguage().getLanguage();
        try {
            return client.translateText(request.texts(), sourceTag, deepLTargetTag, options);
        } catch (AuthorizationException e) {
            throw new InvalidKeyException(e);
        } catch (QuotaExceededException e) {
            throw new MachineTranslationQuotaExceededException("Machine translation quota exceeded", e);
        } catch (TooManyRequestsException e) {
            throw new MachineTranslationUnavailableException(
                    "Machine translation provider rate limited the request", e);
        } catch (ConnectionException e) {
            throw new MachineTranslationUnavailableException("Machine translation provider did not answer in time", e);
        } catch (DeepLException e) {
            throw new MachineTranslationUnavailableException("Machine translation failed", e);
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
            throw new MachineTranslationUnavailableException("Interrupted while waiting for machine translation", e);
        }
    }

    private void recordCharacters(String targetTag, List<TextResult> results) {
        var characters =
                results.stream().mapToLong(TextResult::getBilledCharacters).sum();
        meters.counter(CHARACTERS_METRIC, TARGET_LANGUAGE_TAG, targetTag).increment(characters);
    }

    private static Translation toTranslation(TextResult result) {
        return new Translation(result.getText(), mapToSupportedLocale(result.getDetectedSourceLanguage()));
    }

    /**
     * Maps a DeepL-detected source language ({@code "ES"}, {@code "PT-BR"}, ...) to the closest of Frappé's three
     * supported locales, falling back to English for anything else (DeepL detects more languages than Frappé speaks).
     */
    private static Locale mapToSupportedLocale(String detected) {
        var language = detected.toLowerCase(Locale.ROOT).split("-", 2)[0];
        return SupportedLocales.all().stream()
                .filter(locale -> locale.getLanguage().equals(language))
                .findFirst()
                .orElse(SupportedLocales.FALLBACK);
    }

    /**
     * DeepL requires an explicit region for English and Portuguese target languages ({@code target_lang=en} and
     * {@code target_lang=pt} are rejected client-side by the SDK). An already-explicit, DeepL-supported variant
     * ({@code en-GB}, {@code pt-PT}, {@code es-419}, ...) is sent as-is; any other region is reduced to its bare
     * language, then, if that bare language still needs a region, {@code defaultVariants} supplies one
     * ({@code frappe.translation.deepl.default-variants.*}).
     */
    private String deepLTargetLanguage(Locale target) {
        var explicit = target.toLanguageTag();
        if (SUPPORTED_VARIANTS.contains(explicit)) {
            return explicit;
        }
        var bare = target.getLanguage();
        return defaultVariants.getOrDefault(bare, bare);
    }

    private static Formality formality(TranslationFormality formality) {
        return switch (formality) {
            case DEFAULT -> Formality.Default;
            case LESS -> Formality.Less;
            case MORE -> Formality.More;
        };
    }

    private static MachineTranslationUnavailableException noKeyConfigured() {
        return new MachineTranslationUnavailableException(
                "Machine translation unavailable: no provider key configured");
    }

    /**
     * Internal signal that the configured key was refused, so {@link #execute} can tag the observation
     * {@link TranslationObservation#INVALID_KEY} before sanitizing it into the one exception type callers see.
     */
    private static final class InvalidKeyException extends RuntimeException {

        private static final long serialVersionUID = 1L;

        InvalidKeyException(Throwable cause) {
            super(cause);
        }

        MachineTranslationUnavailableException sanitized() {
            return new MachineTranslationUnavailableException(
                    "Machine translation provider refused the configured key", getCause());
        }
    }
}
