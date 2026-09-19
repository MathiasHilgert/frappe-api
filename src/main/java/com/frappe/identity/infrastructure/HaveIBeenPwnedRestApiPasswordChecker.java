package com.frappe.identity.infrastructure;

import com.frappe.identity.domain.BreachStatus;
import com.frappe.identity.domain.BreachedPasswords;
import com.frappe.identity.domain.Password;
import io.micrometer.context.ContextExecutorService;
import io.micrometer.context.ContextSnapshotFactory;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.security.NoSuchAlgorithmException;
import java.time.Duration;
import java.util.HexFormat;
import java.util.concurrent.ExecutionException;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.TimeoutException;
import java.util.regex.Pattern;
import org.springframework.http.HttpHeaders;
import org.springframework.web.client.RestClient;
import org.springframework.web.client.RestClientException;

/**
 * The breach corpus of Have I Been Pwned's Pwned Passwords range API, by k-anonymity: only the first 5 hex characters
 * of the password's SHA-1 leave the process; the matching suffixes come back and are compared here. Responses are
 * padded ({@code Add-Padding}), so their size does not reveal the prefix either; padding entries have count 0.
 *
 * <p>One total budget ({@code frappe.identity.breached-passwords.timeout}) bounds the whole call, connecting, waiting
 * and reading a slowly dribbled body included: the call runs on a virtual thread (carrying the caller's observation
 * context), and past the budget it is interrupted and the answer is {@link BreachStatus#UNKNOWN}.
 *
 * <p>A sanitizing adapter boundary that fails open: any failure (timeout, network, non-2xx, unreadable answer) is
 * {@link BreachStatus#UNKNOWN}, and is not logged. The HTTP client observation ({@code http.client.requests}) records
 * the request and its error; the password policy then accepts the password.
 */
final class HaveIBeenPwnedRestApiPasswordChecker implements BreachedPasswords, AutoCloseable {

    private static final int PREFIX_LENGTH = 5;

    private static final String USER_AGENT = "frappe-api";

    // One entry of a range answer: the 35 hex characters of a SHA-1 suffix and how often it was seen.
    private static final Pattern RANGE_ENTRY = Pattern.compile("[0-9A-Fa-f]{35}:\\d+");

    private static final HexFormat UPPERCASE_HEX = HexFormat.of().withUpperCase();

    private final RestClient rangeApi;

    private final Duration budget;

    private final ExecutorService calls = ContextExecutorService.wrap(
            Executors.newVirtualThreadPerTaskExecutor(),
            () -> ContextSnapshotFactory.builder().build().captureAll());

    /**
     * Creates the checker.
     *
     * @param properties where the range API answers and how long to wait for it
     * @param builders Boot's client builder
     */
    HaveIBeenPwnedRestApiPasswordChecker(
            IdentityProperties.BreachedPasswordsProperties properties, RestClient.Builder builders) {
        this.budget = properties.timeout();
        this.rangeApi = builders.clone()
                .baseUrl(properties.baseUrl())
                .defaultHeader(HttpHeaders.USER_AGENT, USER_AGENT)
                .build();
    }

    @Override
    public BreachStatus check(Password password) {
        var sha1 = sha1(password);
        var prefix = sha1.substring(0, PREFIX_LENGTH);
        var suffix = sha1.substring(PREFIX_LENGTH);
        var call = calls.submit(() -> rangeOf(prefix));
        try {
            return statusIn(call.get(budget.toNanos(), TimeUnit.NANOSECONDS), suffix);
        } catch (TimeoutException e) {
            // Over budget: interrupting the call makes the client fail, and its observation records the error.
            call.cancel(true);
            return BreachStatus.UNKNOWN;
        } catch (InterruptedException e) {
            call.cancel(true);
            Thread.currentThread().interrupt();
            return BreachStatus.UNKNOWN;
        } catch (ExecutionException e) {
            if (e.getCause() instanceof RestClientException) {
                // Fails open without logging: the client observation already carries the error.
                return BreachStatus.UNKNOWN;
            }
            throw new IllegalStateException("The breach check failed unexpectedly", e.getCause());
        }
    }

    /** Stops the calls still running past their budget. */
    @Override
    public void close() {
        calls.shutdownNow();
    }

    private String rangeOf(String prefix) {
        var range = rangeApi.get()
                .uri("/range/{prefix}", prefix)
                .header("Add-Padding", "true")
                .retrieve()
                .body(String.class);
        return range == null ? "" : range;
    }

    private static BreachStatus statusIn(String range, String suffix) {
        var entries = range.lines()
                .map(String::strip)
                .filter(line -> RANGE_ENTRY.matcher(line).matches())
                .toList();
        if (entries.isEmpty()) {
            // Not an answer of the range API (every answer lists entries, padding included).
            return BreachStatus.UNKNOWN;
        }
        var listed = entries.stream()
                .filter(line -> line.regionMatches(true, 0, suffix, 0, suffix.length()))
                .anyMatch(line -> !line.endsWith(":0"));
        return listed ? BreachStatus.BREACHED : BreachStatus.NOT_FOUND;
    }

    private static String sha1(Password password) {
        try {
            var digest =
                    MessageDigest.getInstance("SHA-1").digest(password.value().getBytes(StandardCharsets.UTF_8));
            return UPPERCASE_HEX.formatHex(digest);
        } catch (NoSuchAlgorithmException e) {
            throw new IllegalStateException("SHA-1 is required of every Java platform", e);
        }
    }
}
