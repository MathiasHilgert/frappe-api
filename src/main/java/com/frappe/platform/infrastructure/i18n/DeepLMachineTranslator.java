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
import com.frappe.platform.i18n.Translation;
import com.frappe.platform.i18n.TranslationFormality;
import com.frappe.platform.i18n.TranslationRequest;
import io.micrometer.core.instrument.MeterRegistry;
import io.micrometer.observation.ObservationRegistry;
import java.util.List;
import java.util.Locale;
import java.util.Optional;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

/**
 * {@link MachineTranslator} over the official DeepL Java SDK. One call, one bounded attempt: the client is configured
 * with {@code maxRetries(0)}, because the SDK's own retry loop would silently extend a call past our timeout, and
 * {@link TranslationObservation#OUTCOME_TAG} already tells callers what happened. HTML/tag handling stays off (the SDK
 * default): callers pass plain text, never markup.
 *
 * <p>Without a configured key ({@code client} empty) {@link #isAvailable()} reports {@code false} and
 * {@link #translate} fails fast with {@link MachineTranslationUnavailableException}, without any HTTP call.
 *
 * <p>Provider failures never reach the caller with the provider's name or response: this is the one place that logs
 * them (once, at ERROR, with the failure as cause), before rethrowing our own {@link MachineTranslationUnavailableException}
 * or {@link MachineTranslationQuotaExceededException}. The web layer (FAPI-14) turns any exception it does not
 * otherwise handle into a generic localized 500.
 */
final class DeepLMachineTranslator implements MachineTranslator {

    private static final Logger log = LoggerFactory.getLogger(DeepLMachineTranslator.class);

    /** Meter name of the billed-characters counter (DeepL bills per character). */
    static final String CHARACTERS_METRIC = "translation.characters";

    /** Low-cardinality tag: the request's target language. */
    static final String TARGET_LANGUAGE_TAG = "target_language";

    private final Optional<DeepLClient> client;

    private final MeterRegistry meters;

    private final ObservationRegistry observations;

    /**
     * Creates the adapter with a configured client.
     *
     * @param client the DeepL client, already pointed at the right server URL and account key
     * @param meters where the characters counter is recorded
     * @param observations where the request observation is recorded
     */
    DeepLMachineTranslator(DeepLClient client, MeterRegistry meters, ObservationRegistry observations) {
        this(Optional.of(client), meters, observations);
    }

    /**
     * Creates the adapter without a key: every call reports unavailable, and the application still starts.
     *
     * @param meters where the characters counter is recorded
     * @param observations where the request observation is recorded
     */
    DeepLMachineTranslator(MeterRegistry meters, ObservationRegistry observations) {
        this(Optional.empty(), meters, observations);
    }

    private DeepLMachineTranslator(
            Optional<DeepLClient> client, MeterRegistry meters, ObservationRegistry observations) {
        this.client = client;
        this.meters = meters;
        this.observations = observations;
    }

    @Override
    public boolean isAvailable() {
        return client.isPresent();
    }

    @Override
    public List<Translation> translate(TranslationRequest request) {
        var targetTag = request.targetLanguage().toLanguageTag();
        var deepLTargetTag = deepLTargetLanguage(request.targetLanguage());
        var observation = TranslationObservation.of(observations, targetTag).start();
        try (var scope = observation.openScope()) {
            var results =
                    translateWith(client.orElseThrow(DeepLMachineTranslator::noKeyConfigured), request, deepLTargetTag);
            observation.lowCardinalityKeyValue(TranslationObservation.OUTCOME_TAG, TranslationObservation.SUCCESS);
            recordCharacters(targetTag, results);
            return results.stream().map(DeepLMachineTranslator::toTranslation).toList();
        } catch (MachineTranslationQuotaExceededException e) {
            observation.error(e);
            observation.lowCardinalityKeyValue(
                    TranslationObservation.OUTCOME_TAG, TranslationObservation.QUOTA_EXCEEDED);
            logFailure(targetTag, TranslationObservation.QUOTA_EXCEEDED, e);
            throw e;
        } catch (MachineTranslationUnavailableException e) {
            observation.error(e);
            observation.lowCardinalityKeyValue(TranslationObservation.OUTCOME_TAG, TranslationObservation.UNAVAILABLE);
            logFailure(targetTag, TranslationObservation.UNAVAILABLE, e);
            throw e;
        } finally {
            observation.stop();
        }
    }

    // The only place this failure is logged: our own sanitized exception is what reaches the caller, but the
    // provider's cause and response stay here, in structured fields, never in a message a client could see. No key
    // configured (cause is null, checked by isAvailable() before every real call) is a normal condition, not logged.
    private static void logFailure(String targetTag, String outcome, RuntimeException failure) {
        if (failure.getCause() == null) {
            return;
        }
        log.atError()
                .addKeyValue(LogFields.TARGET_LANGUAGE, targetTag)
                .addKeyValue(LogFields.TRANSLATION_OUTCOME, outcome)
                .setCause(failure.getCause())
                .log("Machine translation failed ({})", outcome);
    }

    private static List<TextResult> translateWith(
            DeepLClient client, TranslationRequest request, String deepLTargetTag) {
        var options = new TextTranslationOptions()
                .setFormality(formality(request.formality()))
                .setTagHandling(null)
                .setOutlineDetection(true);
        if (request.glossaryReference() != null) {
            options.setGlossaryId(request.glossaryReference());
        }
        var sourceTag = request.sourceLanguage() == null
                ? null
                : request.sourceLanguage().toLanguageTag();
        try {
            return client.translateText(request.texts(), sourceTag, deepLTargetTag, options);
        } catch (AuthorizationException e) {
            throw new MachineTranslationUnavailableException(
                    "Machine translation provider refused the configured key", e);
        } catch (QuotaExceededException e) {
            throw new MachineTranslationQuotaExceededException("Machine translation quota exceeded", e);
        } catch (TooManyRequestsException | ConnectionException e) {
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
        var detected = result.getDetectedSourceLanguage();
        return new Translation(result.getText(), Locale.of(detected.toLowerCase(Locale.ROOT)));
    }

    /**
     * DeepL requires a region variant for English and Portuguese target languages ({@code target_lang=en} and
     * {@code target_lang=pt} are rejected client-side by the SDK); Spanish accepts the bare code. Brazilian Portuguese
     * and US English are Frappé's defaults, matching {@code i18n.md}'s CLDR distance mapping ({@code pt-BR} enables
     * {@code pt}).
     */
    private static String deepLTargetLanguage(Locale target) {
        return switch (target.getLanguage()) {
            case "en" -> "en-US";
            case "pt" -> "pt-BR";
            default -> target.toLanguageTag();
        };
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
}
